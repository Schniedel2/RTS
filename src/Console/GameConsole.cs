using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class GameConsole
{
    private readonly Dictionary<string, Action<string[]>> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _history = new();

    private string _input = "";
    private int _historyIndex = -1;

    public bool IsOpen { get; private set; }

    public IReadOnlyList<string> History => _history;
    public string Input => _input;
    private GraphicsDevice _graphicsDevice;
    private Texture2D _consolePixel;
    public SpriteFont Font { get; set; }

    public GameConsole(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _consolePixel = new Texture2D(_graphicsDevice, 1, 1);
        _consolePixel.SetData(new[] { Color.White });
            
        RegisterCommand("help", _ => PrintHelp());
        RegisterCommand("clear",_ => _history.Clear());
    }

    public void RegisterCommand(
        string name,
        Action<string[]> action)
    {
        _commands[name] = action;
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;

        if (IsOpen)
        {
            _input = "";
            _historyIndex = -1;
        }
    }

    public void AddCharacter(char c)
    {
        if (!IsOpen)
            return;

        _input += c;
    }

    public void Backspace()
    {
        if (!IsOpen || _input.Length == 0)
            return;

        _input = _input[..^1];
    }

    public void Execute()
    {
        if (!IsOpen)
            return;

        string command = _input.Trim();

        if (command.Length == 0)
            return;

        AddHistory("> " + command);

        ParseAndExecute(command);

        _input = "";
        _historyIndex = -1;
    }

    public void HistoryUp()
    {
        if (_history.Count == 0)
            return;

        if (_historyIndex < _history.Count - 1)
            _historyIndex++;

        // ">" entfernen
        string line = _history[
            _history.Count - 1 - _historyIndex];

        if (line.StartsWith("> "))
            line = line[2..];

        _input = line;
    }

    public void HistoryDown()
    {
        if (_historyIndex <= 0)
        {
            _historyIndex = -1;
            _input = "";
            return;
        }

        _historyIndex--;

        string line = _history[
            _history.Count - 1 - _historyIndex];

        if (line.StartsWith("> "))
            line = line[2..];

        _input = line;
    }

    private void ParseAndExecute(string commandLine)
    {
        string[] tokens = Tokenize(commandLine);

        if (tokens.Length == 0)
            return;

        string commandName = tokens[0];

        string[] arguments =
            tokens.Skip(1).ToArray();

        if (!_commands.TryGetValue(
                commandName,
                out var command))
        {
            AddHistory(
                $"Unknown command: {commandName}");

            return;
        }

        try
        {
            command(arguments);
        }
        catch (Exception ex)
        {
            AddHistory(
                $"Error: {ex.Message}");
        }
    }

    private static string[] Tokenize(string input)
    {
        return input
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);
    }

    public void Print(string text)
    {
        AddHistory(text);
    }

    private void AddHistory(string text)
    {
        _history.Add(text);

        // Console nicht unendlich wachsen lassen
        const int maxLines = 100;

        if (_history.Count > maxLines)
        {
            _history.RemoveAt(0);
        }
    }

    private void PrintHelp()
    {
        AddHistory("Commands:");

        foreach (string command in _commands.Keys)
        {
            AddHistory($"  {command}");
        }
    }
 
    public void HandleTextInput(char character)
    {
        if (!IsOpen)
            return;

        if (!char.IsControl(character))
            AddCharacter(character);
    }    

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsOpen)
            return;

        const int consoleHeight = 300;
        const int padding = 10;

        // Hintergrund
        spriteBatch.Draw(
            _consolePixel,
            new Rectangle(
                0,
                0,
                _graphicsDevice.Viewport.Width,
                consoleHeight),
            Color.Black * 0.80f);

        int y = padding;

        // History
        foreach (string line in History)
        {
            spriteBatch.DrawString(
                Font,
                line,
                new Vector2(
                    padding,
                    y),
                Color.White);

            y += Font.LineSpacing;

            // Nicht aus dem Fenster laufen
            if (y > consoleHeight - 40)
                break;
        }

        // Eingabezeile
        spriteBatch.DrawString(
            Font,
            "> " + Input + "_",
            new Vector2(
                padding,
                consoleHeight - Font.LineSpacing - padding),
            Color.White);
    }
}