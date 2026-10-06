if (args.Length > 0 && args[0] == "--bot-client")
{
    if (args.Length != 2) { System.Console.Error.WriteLine("Usage: RTS --bot-client <config.json>"); return 1; }
    return RTS.BotClient.Run(args[1]);
}
using var game = new RTS.Game1();
game.Run();
return 0;
