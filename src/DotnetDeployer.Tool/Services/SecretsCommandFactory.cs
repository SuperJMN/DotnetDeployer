using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using DotnetDeployer.Configuration.Secrets;
using Serilog;

namespace DotnetDeployer.Tool.Services;

internal static class SecretsCommandFactory
{
    public static Command Create(ILogger logger, IKeyringSecretStore? keyring = null)
    {
        var store = keyring ?? new SystemKeyringSecretStore();
        var command = new Command("secrets", "Manage DotnetDeployer secrets in the system keyring");

        command.Add(CreateSetCommand(logger, store));
        command.Add(CreateCheckCommand(logger, store));
        command.Add(CreateDeleteCommand(logger, store));

        return command;
    }

    private static Command CreateSetCommand(ILogger logger, IKeyringSecretStore store)
    {
        var keyArgument = CreateKeyArgument();
        var valueOption = new Option<string?>("--value")
        {
            Description = "Secret value. If omitted, DotnetDeployer prompts without echoing input."
        };

        var command = new Command("set", "Store or update a secret in the system keyring");
        command.Add(keyArgument);
        command.Add(valueOption);

        command.SetAction((ParseResult parseResult) =>
        {
            var key = parseResult.GetValue(keyArgument) ?? "";
            var value = parseResult.GetValue(valueOption) ?? ReadSecret("Secret value: ");

            if (string.IsNullOrEmpty(value))
            {
                logger.Error("Secret value cannot be empty.");
                return 1;
            }

            var result = store.Set(key, value);
            if (result.IsFailure)
            {
                logger.Error("Could not store secret '{Key}': {Error}", key, result.Error);
                return 1;
            }

            logger.Information("Stored secret '{Key}' in the system keyring.", key);
            return 0;
        });

        return command;
    }

    private static Command CreateCheckCommand(ILogger logger, IKeyringSecretStore store)
    {
        var keyArgument = CreateKeyArgument();
        var command = new Command("check", "Check whether a secret exists in the system keyring");
        command.Add(keyArgument);

        command.SetAction((ParseResult parseResult) =>
        {
            var key = parseResult.GetValue(keyArgument) ?? "";
            var result = store.Get(key);
            if (result.IsFailure)
            {
                logger.Error("Secret '{Key}' was not found in the system keyring: {Error}", key, result.Error);
                return 1;
            }

            logger.Information("Secret '{Key}' is present in the system keyring.", key);
            return 0;
        });

        return command;
    }

    private static Command CreateDeleteCommand(ILogger logger, IKeyringSecretStore store)
    {
        var keyArgument = CreateKeyArgument();
        var command = new Command("delete", "Delete a secret from the system keyring");
        command.Add(keyArgument);

        command.SetAction((ParseResult parseResult) =>
        {
            var key = parseResult.GetValue(keyArgument) ?? "";
            var result = store.Delete(key);
            if (result.IsFailure)
            {
                logger.Error("Could not delete secret '{Key}': {Error}", key, result.Error);
                return 1;
            }

            logger.Information("Deleted secret '{Key}' from the system keyring.", key);
            return 0;
        });

        return command;
    }

    private static Argument<string> CreateKeyArgument()
    {
        return new Argument<string>("key")
        {
            Description = "Secret key used by deployer.yaml, for example nuget_api_key."
        };
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadToEnd().TrimEnd('\r', '\n');

        Console.Error.Write(prompt);
        var builder = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                    builder.Length--;

                continue;
            }

            if (!char.IsControl(key.KeyChar))
                builder.Append(key.KeyChar);
        }
    }
}
