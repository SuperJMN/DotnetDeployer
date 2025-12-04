using System.Buffers.Binary;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IOPath = System.IO.Path;

namespace DotnetDeployer.Platforms.Windows;

public class WindowsIconResolver(Maybe<ILogger> logger)
{
    private static readonly Regex IconAttributeRegex = new(@"Icon=""(?<path>[^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WindowIconRegex = new(@"WindowIcon\((?:@)?""(?<path>[^""\r\n]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] PreferredExtensions = [".ico", ".png", ".svg"];
    private static readonly string[] PreferredNames = ["appicon", "icon", "logo"];

    public Result<Maybe<WindowsIcon>> Resolve(Path projectPath)
    {
        var projectFile = projectPath.ToString();
        var projectDirectory = IOPath.GetDirectoryName(projectFile);

        if (projectDirectory is null)
        {
            return Result.Success(Maybe<WindowsIcon>.None);
        }

        var candidate = FindIconCandidate(projectFile, projectDirectory);

        if (candidate.HasNoValue)
        {
            logger.Execute(log => log.Debug("No Windows icon candidate detected for {Project}", projectFile));
            return Result.Success(Maybe<WindowsIcon>.None);
        }

        return PrepareIcon(candidate.Value, projectDirectory)
            .Map(icon => Maybe<WindowsIcon>.From(icon))
            .OnFailureCompensate(error =>
            {
                logger.Execute(log => log.Debug("Failed to prepare Windows icon for {Project}: {Error}", projectFile, error));
                return Result.Success(Maybe<WindowsIcon>.None);
            });
    }

    private Maybe<IconReference> FindIconCandidate(string projectFile, string projectDirectory)
    {
        var projectFileIcon = FindIconInProjectFile(projectFile);
        if (projectFileIcon.HasValue)
        {
            return projectFileIcon;
        }

        var xamlIcon = FindIconInXaml(projectDirectory);
        if (xamlIcon.HasValue)
        {
            return xamlIcon;
        }

        var codeIcon = FindIconInCodeBehind(projectDirectory);
        if (codeIcon.HasValue)
        {
            return codeIcon;
        }

        return FindIconByScanning(projectDirectory);
    }

    private Maybe<IconReference> FindIconInProjectFile(string projectFile)
    {
        if (!File.Exists(projectFile))
        {
            return Maybe<IconReference>.None;
        }

        try
        {
            var document = XDocument.Load(projectFile);
            var applicationIcon = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "ApplicationIcon");
            var value = applicationIcon?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Maybe<IconReference>.From(new IconReference(value, false));
            }
        }
        catch (Exception ex)
        {
            logger.Execute(log => log.Debug(ex, "Unable to parse project file {ProjectFile} while looking for ApplicationIcon", projectFile));
        }

        return Maybe<IconReference>.None;
    }

    private Maybe<IconReference> FindIconInXaml(string projectDirectory)
    {
        foreach (var file in EnumerateFiles(projectDirectory, "*.axaml"))
            try
            {
                var content = File.ReadAllText(file);
                var match = IconAttributeRegex.Match(content);
                if (match.Success)
                {
                    var value = match.Groups["path"].Value.Trim();
                    var reference = InterpretResourceReference(value);
                    if (reference.HasValue)
                    {
                        return reference;
                    }

                    return Maybe<IconReference>.From(new IconReference(value, false));
                }
            }
            catch (Exception ex)
            {
                logger.Execute(log => log.Debug(ex, "Failed to inspect {File} while looking for Icon attribute", file));
            }

        return Maybe<IconReference>.None;
    }

    private Maybe<IconReference> FindIconInCodeBehind(string projectDirectory)
    {
        foreach (var file in EnumerateFiles(projectDirectory, "*.cs"))
            try
            {
                var content = File.ReadAllText(file);
                var match = WindowIconRegex.Match(content);
                if (match.Success)
                {
                    var value = match.Groups["path"].Value.Trim();
                    var reference = InterpretResourceReference(value);
                    if (reference.HasValue)
                    {
                        return reference;
                    }

                    return Maybe<IconReference>.From(new IconReference(value, false));
                }
            }
            catch (Exception ex)
            {
                logger.Execute(log => log.Debug(ex, "Failed to inspect {File} while looking for WindowIcon construction", file));
            }

        return Maybe<IconReference>.None;
    }

    private Maybe<IconReference> FindIconByScanning(string projectDirectory)
    {
        var candidateDirectories = GetCandidateDirectories(projectDirectory);

        var files = candidateDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => EnumerateFiles(directory, "*.*"))
            .Select(file => new { file, extension = IOPath.GetExtension(file) })
            .Where(item => PreferredExtensions.Contains(item.extension, StringComparer.OrdinalIgnoreCase))
            .Where(item => FileLooksLikeIcon(item.file));

        var best = files
            .OrderBy(item => Array.IndexOf(PreferredExtensions, item.extension.ToLowerInvariant()))
            .ThenByDescending(item => new FileInfo(item.file).Length)
            .Select(item => item.file)
            .FirstOrDefault();

        if (best is null)
        {
            return Maybe<IconReference>.None;
        }

        return Maybe<IconReference>.From(new IconReference(best, true));
    }

    private Result<WindowsIcon> PrepareIcon(IconReference reference, string projectDirectory)
    {
        var iconPath = reference.ToAbsolute(projectDirectory);

        if (!File.Exists(iconPath))
        {
            return Result.Failure<WindowsIcon>($"Icon candidate '{iconPath}' does not exist");
        }

        var extension = IOPath.GetExtension(iconPath).ToLowerInvariant();

        return extension switch
        {
            ".ico" => Result.Success(new WindowsIcon(iconPath, false)),
            ".png" => CreateIconFromPng(iconPath),
            ".svg" => ResolveSvg(iconPath),
            _ => Result.Failure<WindowsIcon>($"Unsupported icon format '{extension}' for Windows packaging")
        };
    }

    private Result<WindowsIcon> CreateIconFromPng(string pngPath)
    {
        return Result.Try(() => File.ReadAllBytes(pngPath))
            .Bind(bytes => CreateIconFromPngBytes(bytes));
    }

    private Result<WindowsIcon> CreateIconFromPngBytes(byte[] pngBytes)
    {
        return GetPngDimensions(pngBytes)
            .Bind(dimensions => CreateIconFile(pngBytes, dimensions.Width, dimensions.Height));
    }

    private Result<WindowsIcon> ResolveSvg(string svgPath)
    {
        var fallback = FindRasterFallback(svgPath);

        if (fallback.HasNoValue)
        {
            logger.Execute(log => log.Debug("Skipping SVG icon at {SvgPath} because no raster fallback was found", svgPath));
            return Result.Failure<WindowsIcon>("Unable to prepare a Windows icon from SVG without a PNG fallback");
        }

        logger.Execute(log => log.Debug("Using raster fallback {Fallback} for SVG icon {SvgPath}", fallback.Value, svgPath));
        return CreateIconFromPng(fallback.Value);
    }

    private Result<(byte Width, byte Height)> GetPngDimensions(byte[] pngBytes)
    {
        return Result.Try(() =>
        {
            var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (pngBytes.Length < 24)
            {
                throw new InvalidOperationException("PNG data is too short to contain dimensions");
            }

            for (var i = 0; i < signature.Length; i++)
                if (pngBytes[i] != signature[i])
                {
                    throw new InvalidOperationException("Invalid PNG signature");
                }

            var span = new ReadOnlySpan<byte>(pngBytes);
            var width = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(20, 4));

            if (width == 0 || height == 0)
            {
                throw new InvalidOperationException("PNG dimensions cannot be zero");
            }

            var widthByte = width >= 256 ? (byte)0 : (byte)width;
            var heightByte = height >= 256 ? (byte)0 : (byte)height;

            return (widthByte, heightByte);
        });
    }

    private Result<WindowsIcon> CreateIconFile(byte[] imageBytes, byte width, byte height)
    {
        return Result.Try(() =>
        {
            var tempFile = IOPath.Combine(IOPath.GetTempPath(), $"{Guid.NewGuid():N}.ico");
            using var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write(width == 0 ? (byte)0 : width);
            writer.Write(height == 0 ? (byte)0 : height);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write((ushort)32);
            writer.Write(imageBytes.Length);
            writer.Write(6 + 16);
            writer.Write(imageBytes);

            return new WindowsIcon(tempFile, true);
        });
    }

    private IEnumerable<string> GetCandidateDirectories(string projectDirectory)
    {
        var candidates = new List<string>
        {
            projectDirectory,
            IOPath.Combine(projectDirectory, "Assets"),
            IOPath.Combine(projectDirectory, "Assets", "Icons"),
            IOPath.Combine(projectDirectory, "Assets", "Images"),
            IOPath.Combine(projectDirectory, "Resources"),
            IOPath.Combine(projectDirectory, "Resources", "Icons"),
            IOPath.Combine(projectDirectory, "Images"),
            IOPath.Combine(projectDirectory, "Icons")
        };

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private Maybe<string> FindRasterFallback(string svgPath)
    {
        var direct = IOPath.ChangeExtension(svgPath, ".png");
        if (direct is not null && File.Exists(direct))
        {
            return Maybe<string>.From(direct);
        }

        var directory = IOPath.GetDirectoryName(svgPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return Maybe<string>.None;
        }

        var svgName = IOPath.GetFileNameWithoutExtension(svgPath);
        var matching = Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => string.Equals(IOPath.GetFileNameWithoutExtension(file), svgName, StringComparison.OrdinalIgnoreCase));

        if (matching is not null)
        {
            return Maybe<string>.From(matching);
        }

        var any = Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return any is not null ? Maybe<string>.From(any) : Maybe<string>.None;
    }

    private static bool FileLooksLikeIcon(string path)
    {
        var fileName = IOPath.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return PreferredNames.Any(name => fileName.Contains(name, StringComparison.Ordinal)) ||
               path.Contains("icon", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string root, string searchPattern)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files) yield return file;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = IOPath.GetFileName(directory);
                if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                stack.Push(directory);
            }
        }
    }

    private Maybe<IconReference> InterpretResourceReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Maybe<IconReference>.None;
        }

        var trimmed = value.Trim();

        if (trimmed.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            var remaining = trimmed.Substring("avares://".Length);
            var slashIndex = remaining.IndexOf('/') + 1;
            if (slashIndex <= 0 || slashIndex >= remaining.Length)
            {
                return Maybe<IconReference>.None;
            }

            var relative = remaining[slashIndex..];
            return Maybe<IconReference>.From(new IconReference(relative.Replace('/', IOPath.DirectorySeparatorChar), false));
        }

        if (trimmed.StartsWith("resm:", StringComparison.OrdinalIgnoreCase))
        {
            var resource = trimmed.Substring("resm:".Length);
            var queryIndex = resource.IndexOf('?');
            if (queryIndex >= 0)
            {
                resource = resource[..queryIndex];
            }

            var segments = resource.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return Maybe<IconReference>.None;
            }

            var withoutAssembly = segments.Skip(1).ToList();
            if (withoutAssembly.Count >= 2)
            {
                var fileSegments = withoutAssembly.TakeLast(2).ToArray();
                withoutAssembly = withoutAssembly.SkipLast(2).ToList();
                withoutAssembly.Add(string.Join('.', fileSegments));
            }

            var relative = IOPath.Combine(withoutAssembly.ToArray());
            return Maybe<IconReference>.From(new IconReference(relative, false));
        }

        if (IOPath.IsPathRooted(trimmed))
        {
            if (LooksLikeProjectRelativeRootedPath(trimmed))
            {
                var relative = trimmed.TrimStart('/', '\\');
                return string.IsNullOrWhiteSpace(relative)
                    ? Maybe<IconReference>.None
                    : Maybe<IconReference>.From(new IconReference(relative, false));
            }

            return Maybe<IconReference>.From(new IconReference(trimmed, true));
        }

        trimmed = trimmed.TrimStart('/', '\\');
        return string.IsNullOrWhiteSpace(trimmed)
            ? Maybe<IconReference>.None
            : Maybe<IconReference>.From(new IconReference(trimmed, false));
    }

    private static bool LooksLikeProjectRelativeRootedPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Length >= 3 && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
        {
            return false;
        }

        var startsWithSlash = value[0] == '/' || value[0] == '\\';
        return startsWithSlash && !File.Exists(value);
    }

    private readonly record struct IconReference(string Value, bool IsAbsolute)
    {
        public string ToAbsolute(string projectDirectory)
        {
            if (IsAbsolute)
            {
                return Value;
            }

            return IOPath.GetFullPath(IOPath.Combine(projectDirectory, Value.Replace('/', IOPath.DirectorySeparatorChar)));
        }
    }
}