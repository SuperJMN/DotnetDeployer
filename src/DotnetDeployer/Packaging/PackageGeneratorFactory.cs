using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration;
using DotnetDeployer.Domain;
using DotnetDeployer.Packaging.Android;
using DotnetDeployer.Packaging.Linux;
using DotnetDeployer.Packaging.Mac;
using DotnetDeployer.Packaging.Windows;
using Serilog;
using Zafiro.Commands;
using ICommand = Zafiro.Commands.ICommand;

namespace DotnetDeployer.Packaging;

/// <summary>
/// Factory for creating package generators.
/// </summary>
public class PackageGeneratorFactory
{
    private readonly Dictionary<PackageType, IPackageGenerator> generators;
    private readonly ICommand cmd;

    public PackageGeneratorFactory(ICommand? command = null)
    {
        cmd = command ?? new Command(Maybe<ILogger>.None);

        generators = new Dictionary<PackageType, IPackageGenerator>
        {
            // Linux
            [PackageType.AppImage] = new AppImageGenerator(),
            [PackageType.Deb] = new DebGenerator(),
            [PackageType.Rpm] = new RpmGenerator(),
            [PackageType.Flatpak] = new FlatpakGenerator(),

            // Windows
            [PackageType.ExeSfx] = new ExeSfxGenerator(),
            [PackageType.ExeSetup] = new ExeSetupGenerator(),
            [PackageType.Msix] = new MsixGenerator(),

            // Mac
            [PackageType.Dmg] = new DmgGenerator(),

            // Android (these use ICommand)
            [PackageType.Apk] = new ApkGenerator(cmd),
            [PackageType.Aab] = new AabGenerator(cmd)
        };
    }

    public IPackageGenerator GetGenerator(PackageType type)
    {
        if (!generators.TryGetValue(type, out var generator))
        {
            throw new ArgumentException($"No generator registered for package type: {type}");
        }

        return generator;
    }

    public IPackageGenerator GetGenerator(PackageFormatConfig formatConfig)
    {
        var type = formatConfig.GetPackageType();

        if (formatConfig.Signing is not null)
        {
            return type switch
            {
                PackageType.Apk => new ApkGenerator(cmd, formatConfig.Signing),
                PackageType.Aab => new AabGenerator(cmd, formatConfig.Signing),
                _ => throw new ArgumentException($"Signing configuration is only supported for Android package types (Apk, Aab), not '{type}'.")
            };
        }

        return GetGenerator(type);
    }

    public IEnumerable<PackageType> SupportedTypes => generators.Keys;
}
