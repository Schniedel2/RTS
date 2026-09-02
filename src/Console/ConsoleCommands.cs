using Microsoft.Xna.Framework;
using System;

namespace RTS;

public class ConsoleCommands
{
    private readonly GameConsole _console;
    private readonly GameWorld _world;
    private readonly RTSGame _rtsGame;
    private readonly PlayerHandler _localPlayer;

    public ConsoleCommands(
        GameConsole console,
        RTSGame rtsGame)
    {
        _console = console;
        _rtsGame = rtsGame;
        _world = rtsGame.World;
        _localPlayer = rtsGame.LocalPlayer;

        RegisterCommands();
    }

    private void RegisterCommands()
    {
        _console.RegisterCommand(
            "spawn",
            Spawn);
    }

    private void Spawn(string[] args)
    {
        if (args.Length == 0)
        {
            _console.Print("Usage: spawn <unit>");
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "tank":
                _world.SpawnTank(new Vector3(_localPlayer.MouseWorldPosition.X, 64.0f, _localPlayer.MouseWorldPosition.Y));
                break;

            case "soldier":
                _world.SpawnSoldier(new Vector3(_localPlayer.MouseWorldPosition.X, 64.0f, _localPlayer.MouseWorldPosition.Y)    );
                break;

            case "car":
                _world.SpawnCar(new Vector3(_localPlayer.MouseWorldPosition.X, 64.0f, _localPlayer.MouseWorldPosition.Y));
                break;

            default:
                _console.Print(
                    $"Unknown unit: {args[0]}");
                break;
        }
    }
}