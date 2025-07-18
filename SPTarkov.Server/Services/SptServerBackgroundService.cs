using System.Net;
using System.Runtime;
using System.Security.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace SPTarkov.Server.Services;

public class SptServerBackgroundService(
    IReadOnlyList<SptMod> loadedMods,
    BundleLoader bundleLoader,
    App app,
    IServer kestrelServer,
    CertificateHelper certHelper
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ProgramStatics.MODS())
        {
            foreach (var mod in loadedMods)
            {
                if (mod.ModMetadata?.IsBundleMod == true)
                {
                    // Convert to relative path
                    var relativeModPath = Path.GetRelativePath(
                            Directory.GetCurrentDirectory(),
                            mod.Directory
                        )
                        .Replace('\\', '/');

                    bundleLoader.AddBundles(relativeModPath);
                }
            }
        }

        await app.InitializeAsync();

        // Run garbage collection now the server is ready to start
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

        // Im sorry for this, but there's no other way to allow modders to change these values after startup otherwise :(
        var options = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.DefinedTypes)
            .SingleOrDefault(t => t.FullName == "Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl")!
            .GetProperty("Options")!
            .GetValue(kestrelServer) as KestrelServerOptions;

        // Dont inject the config server, modders may do changes to it so we need to wait after the app start above
        var httpConfig = options!
            .ApplicationServices.GetService<ConfigServer>()
            ?.GetConfig<HttpConfig>()!;
        options.Listen(
            IPAddress.Parse(httpConfig.Ip),
            httpConfig.Port,
            listenOptions =>
            {
                listenOptions.UseHttps(opts =>
                {
                    opts.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    opts.ServerCertificate = certHelper.LoadOrGenerateCertificatePfx();
                    opts.ClientCertificateMode = ClientCertificateMode.NoCertificate;
                });
            }
        );
    }
}
