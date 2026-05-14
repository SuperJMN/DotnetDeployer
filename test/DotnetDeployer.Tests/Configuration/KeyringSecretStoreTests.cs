using CSharpFunctionalExtensions;
using DotnetDeployer.Configuration.Secrets;

namespace DotnetDeployer.Tests.Configuration;

public class KeyringSecretStoreTests
{
    [Fact]
    public void SecretTool_Set_StoresSecretThroughStandardInput()
    {
        var runner = new RecordingKeyringCommandRunner(Result.Success(""));
        var store = new SecretToolKeyringSecretStore(runner);

        var result = store.Set("nuget_api_key", "secret-value");

        Assert.True(result.IsSuccess);
        Assert.Equal("secret-tool", runner.FileName);
        Assert.Equal(["store", "--label", "DotnetDeployer nuget_api_key", "application", "DotnetDeployer", "key", "nuget_api_key"], runner.Arguments);
        Assert.Equal("secret-value", runner.StandardInput);
    }

    [Fact]
    public void SecretTool_Get_ReturnsTrimmedValue()
    {
        var runner = new RecordingKeyringCommandRunner(Result.Success("secret-value\n"));
        var store = new SecretToolKeyringSecretStore(runner);

        var result = store.Get("github_token");

        Assert.True(result.IsSuccess);
        Assert.Equal("secret-value", result.Value);
        Assert.Equal(["lookup", "application", "DotnetDeployer", "key", "github_token"], runner.Arguments);
    }

    [Fact]
    public void SecurityTool_Set_UsesMacOsKeychainCommand()
    {
        var runner = new RecordingKeyringCommandRunner(Result.Success(""));
        var store = new SecurityToolKeyringSecretStore(runner);

        var result = store.Set("github_token", "secret-value");

        Assert.True(result.IsSuccess);
        Assert.Equal("security", runner.FileName);
        Assert.Equal(["add-generic-password", "-s", "DotnetDeployer", "-a", "github_token", "-w", "secret-value", "-U"], runner.Arguments);
    }

    [Fact]
    public void SecretTool_Get_EmptyValue_Fails()
    {
        var runner = new RecordingKeyringCommandRunner(Result.Success(""));
        var store = new SecretToolKeyringSecretStore(runner);

        var result = store.Get("nuget_api_key");

        Assert.True(result.IsFailure);
        Assert.Contains("empty", result.Error);
    }

    [Fact]
    public void Store_EmptyKey_Fails()
    {
        var runner = new RecordingKeyringCommandRunner(Result.Success(""));
        var store = new SecretToolKeyringSecretStore(runner);

        var result = store.Set("", "secret-value");

        Assert.True(result.IsFailure);
        Assert.Contains("Secret key is required", result.Error);
        Assert.Null(runner.FileName);
    }

    private sealed class RecordingKeyringCommandRunner : IKeyringCommandRunner
    {
        private readonly Result<string> result;

        public RecordingKeyringCommandRunner(Result<string> result)
        {
            this.result = result;
        }

        public string? FileName { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public string? StandardInput { get; private set; }

        public Result<string> Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
        {
            FileName = fileName;
            Arguments = arguments;
            StandardInput = standardInput;
            return result;
        }
    }
}
