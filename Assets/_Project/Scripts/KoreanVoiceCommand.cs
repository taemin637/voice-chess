using System;
using System.Collections.Generic;
using System.Text;

public enum PieceVoiceCommand : byte
{
    MoveForward,
    MoveBackward,
    Stop,
    TurnLeft,
    TurnRight
}

public static class KoreanVoiceCommand
{
    private static readonly Dictionary<string, PieceVoiceCommand> Aliases = new()
    {
        ["앞으로이동"] = PieceVoiceCommand.MoveForward,
        ["앞으로가"] = PieceVoiceCommand.MoveForward,
        ["계속앞으로"] = PieceVoiceCommand.MoveForward,
        ["계속가"] = PieceVoiceCommand.MoveForward,
        ["전진"] = PieceVoiceCommand.MoveForward,

        ["뒤로이동"] = PieceVoiceCommand.MoveBackward,
        ["뒤로가"] = PieceVoiceCommand.MoveBackward,
        ["계속뒤로"] = PieceVoiceCommand.MoveBackward,
        ["후진"] = PieceVoiceCommand.MoveBackward,

        ["즉시멈춰"] = PieceVoiceCommand.Stop,
        ["멈춰"] = PieceVoiceCommand.Stop,
        ["정지"] = PieceVoiceCommand.Stop,
        ["이동중지"] = PieceVoiceCommand.Stop,

        ["왼쪽회전"] = PieceVoiceCommand.TurnLeft,
        ["왼쪽으로돌아"] = PieceVoiceCommand.TurnLeft,
        ["좌회전"] = PieceVoiceCommand.TurnLeft,

        ["오른쪽회전"] = PieceVoiceCommand.TurnRight,
        ["오른쪽으로돌아"] = PieceVoiceCommand.TurnRight,
        ["옆으로돌아"] = PieceVoiceCommand.TurnRight,
        ["우회전"] = PieceVoiceCommand.TurnRight
    };

    public static IReadOnlyCollection<string> PhraseHints { get; } = new[]
    {
        "앞으로 이동",
        "앞으로 가",
        "계속 앞으로",
        "계속 가",
        "전진",
        "뒤로 이동",
        "뒤로 가",
        "계속 뒤로",
        "후진",
        "즉시 멈춰",
        "멈춰",
        "정지",
        "이동 중지",
        "왼쪽 회전",
        "왼쪽으로 돌아",
        "좌회전",
        "오른쪽 회전",
        "오른쪽으로 돌아",
        "옆으로 돌아",
        "우회전"
    };

    public static bool TryParse(string recognizedText, out PieceVoiceCommand command)
    {
        return Aliases.TryGetValue(Normalize(recognizedText), out command);
    }

    public static string GetDisplayName(PieceVoiceCommand command)
    {
        return command switch
        {
            PieceVoiceCommand.MoveForward => "앞으로 이동",
            PieceVoiceCommand.MoveBackward => "뒤로 이동",
            PieceVoiceCommand.Stop => "멈춰",
            PieceVoiceCommand.TurnLeft => "왼쪽 회전",
            PieceVoiceCommand.TurnRight => "오른쪽 회전",
            _ => command.ToString()
        };
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);

        foreach (char character in text.Trim())
        {
            if (char.IsWhiteSpace(character) ||
                char.IsPunctuation(character) ||
                char.IsSymbol(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
