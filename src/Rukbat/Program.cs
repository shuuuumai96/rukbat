using Rukbat;

if (args.Length != 0 && args[0] == "test")
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
}
if (args is ["lsp"])
{
    return LanguageServer.Run(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
}
return Cli.Run(args, Console.Out, Console.Error);
