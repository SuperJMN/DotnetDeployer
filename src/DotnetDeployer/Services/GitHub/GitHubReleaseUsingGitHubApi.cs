using DotnetDeployer.Core;
using Octokit;
using Zafiro.Mixins;

namespace DotnetDeployer.Services.GitHub;

public class GitHubReleaseUsingGitHubApi(Context context, IEnumerable<INamedByteSource> files, string ownerName, string repositoryName, string apiKey)
{
    public async Task<Result> CreateRelease(string tagName, string releaseName, string releaseBody, bool isDraft = false, bool isPrerelease = false)
    {
        var releaseResult = await CreateReleaseOnly(tagName, releaseName, releaseBody, isDraft, isPrerelease);
        if (releaseResult.IsFailure)
        {
            return Result.Failure(releaseResult.Error);
        }

        var release = releaseResult.Value;
        var client = CreateClient();

        try
        {
            foreach (var file in files.ToList())
            {
                var uploadResult = await UploadAsset(client, release, file);
                if (uploadResult.IsFailure)
                {
                    return Result.Failure(uploadResult.Error);
                }
            }

            context.Logger.Execute(logger => logger.Information("Successfully created GitHub release {ReleaseName}", releaseName));
            return Result.Success(release);
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to upload assets: {ex.Message}");
        }
    }

    public async Task<Result<Release>> CreateReleaseOnly(string tagName, string releaseName, string releaseBody, bool isDraft = false, bool isPrerelease = false)
    {
        try
        {
            context.Logger.Execute(logger => logger.Information("Creating GitHub release {ReleaseName} for tag {TagName}", releaseName, tagName));

            var client = CreateClient();

            var newRelease = new NewRelease(tagName)
            {
                Name = releaseName,
                Body = releaseBody,
                Draft = isDraft,
                Prerelease = isPrerelease
            };

            var release = await client.Repository.Release.Create(ownerName, repositoryName, newRelease);
            context.Logger.Information("Created release with ID {ReleaseId}", release.Id);

            return Result.Success(release);
        }
        catch (Exception ex)
        {
             var errorMessage = $"Failed to create GitHub release: {ex.Message}";
             context.Logger.Execute(logger => logger.Error(ex, errorMessage));
             return Result.Failure<Release>(errorMessage);
        }
    }

    public async Task<Result> UploadAsset(GitHubClient client, Release release, INamedByteSource file)
    {
        try
        {
            context.Logger.Execute(logger => logger.Information("Uploading asset {FileName}", file.Name));

            var assetUpload = new ReleaseAssetUpload
            {
                FileName = file.Name,
                ContentType = GetContentType(file.Name),
                RawData = file.ToStreamSeekable()
            };

            await client.Repository.Release.UploadAsset(release, assetUpload);

            context.Logger.Execute(logger => logger.Information("Successfully uploaded asset {FileName}", file.Name));
            return Result.Success();
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to upload release asset {file.Name}: {ex.Message}";
            context.Logger.Execute(logger => logger.Error(ex, errorMessage));
            return Result.Failure(errorMessage);
        }
    }

    public GitHubClient CreateClient()
    {
        return new GitHubClient(new ProductHeaderValue("DotnetPackaging"))
        {
            Credentials = new Credentials(apiKey)
        };
    }
    
    private static string GetContentType(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".zip" => "application/zip",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".tar.gz" => "application/gzip",
            ".exe" => "application/octet-stream",
            ".msi" => "application/x-msi",
            ".deb" => "application/vnd.debian.binary-package",
            ".rpm" => "application/x-rpm",
            ".appimage" => "application/x-executable",
            ".dmg" => "application/x-apple-diskimage",
            _ => "application/octet-stream"
        };
    }
}