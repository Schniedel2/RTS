using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class GameConsole
{
    private readonly Dictionary<string, Action<string[]>> _commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string[], Task>> _asyncCommands =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _history = new();
    private List<string> _commandsHistory = new();

    private string _input = "";
    private int _historyIndex = -1;

    public bool IsOpen { get; private set; }

    public IReadOnlyList<string> History => _history;
    public string Input => _input;
    private Texture2D _consolePixel;
    public SpriteFont Font { get; } = Globals._debugFont;
    int _autoCompleteIndex = 0;
    string _autoCompleteText = "";
    int _cursorPosition = 0;

    public GameConsole()
    {
        _consolePixel = new Texture2D(Globals.GraphicsDevice, 1, 1);
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

    public void RegisterAsyncCommand(
        string name,
        Func<string[], Task> action)
    {
        _asyncCommands[name] = action;
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        _cursorPosition = 0;

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

        _cursorPosition++;
        _input = _input.Insert(_cursorPosition - 1, c.ToString());
    }

    public void Backspace()
    {
        if (!IsOpen || _input.Length == 0)
            return;

        if (_cursorPosition > 0)
        {
            _input = _input.Remove(_cursorPosition - 1, 1);
            _cursorPosition--;
        }
    }

    public void Delete()
    {
        if (!IsOpen || _input.Length == 0)
            return;

        if (_cursorPosition < _input.Length)
        {
            _input = _input.Remove(_cursorPosition, 1);
        }
    }

    public void Execute()
    {
        if (!IsOpen)
            return;

        string command = _input.Trim();

        if (command.Length == 0)
            return;

        AddHistory("> " + command);

        _ = ExecuteCommandAsync(command);

        _input = "";
        _cursorPosition = 0;
        _historyIndex = -1;
    }

    public void ExecuteCommand(string commandLine)
    {
        _ = ExecuteCommandAsync(commandLine);
    }

    public async Task ExecuteCommandAsync(string commandLine)
    {
        string command = commandLine.Trim();

        if (command.Length == 0)
            return;

        AddHistory("> " + command);
        await ParseAndExecuteAsync(command);
    }

    public void HistoryUp()
    {
        if (_commandsHistory.Count == 0)
            return;

        if (_historyIndex < _commandsHistory.Count - 1)
            _historyIndex++;

        // ">" entfernen
        string line = _commandsHistory[
            _commandsHistory.Count - 1 - _historyIndex];

        _input = line;
        _cursorPosition = _input.Length;
    }

    public void HistoryDown()
    {
        if (_historyIndex <= 0)
        {
            _historyIndex = -1;
            _input = "";
            _cursorPosition = 0;
            return;
        }

        _historyIndex--;

        string line = _commandsHistory[
            _commandsHistory.Count - 1 - _historyIndex];

        _input = line;
        _cursorPosition = _input.Length;
    }

    private async Task ParseAndExecuteAsync(string commandLine)
    {
        _commandsHistory.Add(commandLine);
        if (_commandsHistory.Count > 100)
            _commandsHistory.RemoveAt(0);

        string[] tokens = Tokenize(commandLine);

        if (tokens.Length == 0)
            return;

        string commandName = tokens[0];

        string[] arguments =
            tokens.Skip(1).ToArray();

        if (_asyncCommands.TryGetValue(commandName, out Func<string[], Task>? asyncCommand))
        {
            try
            {
                await asyncCommand(arguments);
            }
            catch (Exception ex)
            {
                AddHistory($"Error: {ex.Message}");
            }

            return;
        }

        if (!_commands.TryGetValue(commandName, out Action<string[]>? command))
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
        {
            AddCharacter(character);
            _autoCompleteText = Input;
            _autoCompleteIndex = 0;
        }

        if (char.IsControl(character))
            HandleControlCharacter(character);
    }    

    void HandleControlCharacter(char character)
    {
        //  handle ESC to close
        if (character == '\u001b') // ESC to clear input
        {
            _input = "";
            _cursorPosition = 0;
            _historyIndex = -1;
            return;
        }

        //  handle TAB for auto-completion
        if (character == '\t')
        {
            int stop = _autoCompleteIndex;
            do
            {            
                List<string> commands = _commands.Keys.ToList();
                commands.AddRange(_asyncCommands.Keys.ToList());

                _autoCompleteIndex++;
                _autoCompleteIndex %= commands.Count;

                if (commands[_autoCompleteIndex].StartsWith(_autoCompleteText, StringComparison.InvariantCultureIgnoreCase))
                {
                    _input = commands[_autoCompleteIndex];
                    _cursorPosition = _input.Length;
                    return;
                }
            }
            while (_autoCompleteIndex != stop);
        }
    }

    public void CursorLeft()
    {
        if (_cursorPosition > 0)
            _cursorPosition--;
    }

    public void CursorRight()
    {
        if (_cursorPosition < Input.Length)
            _cursorPosition++;
    }

    public void CursorHome()
    {
        _cursorPosition = 0;
    }

    public void CursorEnd()
    {
        _cursorPosition = Input.Length;
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
                Globals.GraphicsDevice.Viewport.Width,
                consoleHeight),
            Color.Black * 0.80f);

        int y = padding;

        // History
        for (int i = Math.Max(0, History.Count - 9); i < History.Count; i++)
        {
            string line = History[i];
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
        Vector2 pos = new Vector2(
                padding,
                consoleHeight - Font.LineSpacing - padding);

        string inputLine = "> " + _input;
        char cursorChar = 'W';
        if (_cursorPosition < _input.Length)
            cursorChar = _input[_cursorPosition];

        spriteBatch.DrawString(
            Font,
            inputLine,
            pos,
            Color.White);

        float inputWidth = Font.MeasureString(inputLine.Substring(0, 2 + _cursorPosition)).X;
        float cursorWidth = Font.MeasureString(cursorChar.ToString()).X;

        pos.X += inputWidth - 2;
        spriteBatch.DrawString(
            Font,
            "|",
            pos,
            Color.GreenYellow);

    }
}