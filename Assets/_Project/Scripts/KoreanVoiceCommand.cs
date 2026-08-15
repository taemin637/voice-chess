using System;
using System.Collections.Generic;
using System.Text;

public enum PieceVoiceCommand : byte
{
    MoveForward,
    MoveBackward,
    Stop,
    TurnLeft,
    TurnRight,
    MoveLeft,
    MoveRight,
    MoveUpperRight,
    MoveUpperLeft,
    MoveLowerRight,
    MoveLowerLeft,
    SkillPrimary,
    SkillSecondary,
    Charge
}

public static class KoreanVoiceCommand
{
    private static readonly Dictionary<string, PieceVoiceCommand> Aliases = new()
    {
        ["돌진"] = PieceVoiceCommand.Charge,
        ["돌진해"] = PieceVoiceCommand.Charge,
        ["돌진해줘"] = PieceVoiceCommand.Charge,
        ["공격"] = PieceVoiceCommand.Charge,
        ["공격해"] = PieceVoiceCommand.Charge,
        ["공격해줘"] = PieceVoiceCommand.Charge
    };

    public static IReadOnlyCollection<string> PhraseHints { get; } = new[]
    {
        "돌진",
        "돌진해",
        "돌진해 줘",
        "공격",
        "공격해",
        "공격해 줘"
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
            PieceVoiceCommand.MoveLeft => "왼쪽으로 이동",
            PieceVoiceCommand.MoveRight => "오른쪽으로 이동",
            PieceVoiceCommand.MoveUpperRight => "오른쪽 위로 이동",
            PieceVoiceCommand.MoveUpperLeft => "왼쪽 위로 이동",
            PieceVoiceCommand.MoveLowerRight => "오른쪽 아래로 이동",
            PieceVoiceCommand.MoveLowerLeft => "왼쪽 아래로 이동",
            PieceVoiceCommand.SkillPrimary => "주 스킬",
            PieceVoiceCommand.SkillSecondary => "보조 스킬",
            PieceVoiceCommand.Charge => "돌진",
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
