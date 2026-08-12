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
        ["왼쪽으로가"] = PieceVoiceCommand.MoveLeft,
        ["왼쪽으로돌아"] = PieceVoiceCommand.TurnLeft,
        ["좌회전"] = PieceVoiceCommand.TurnLeft,

        ["오른쪽회전"] = PieceVoiceCommand.TurnRight,
        ["오른쪽으로가"] = PieceVoiceCommand.MoveRight,
        ["오른쪽으로돌아"] = PieceVoiceCommand.TurnRight,
        ["옆으로돌아"] = PieceVoiceCommand.TurnRight,
        ["우회전"] = PieceVoiceCommand.TurnRight,

        ["오른쪽위로가"] = PieceVoiceCommand.MoveUpperRight,
        ["왼쪽위로가"] = PieceVoiceCommand.MoveUpperLeft,
        ["오른쪽아래로가"] = PieceVoiceCommand.MoveLowerRight,
        ["왼쪽아래로가"] = PieceVoiceCommand.MoveLowerLeft,

        ["주스킬사용"] = PieceVoiceCommand.SkillPrimary,
        ["첫번째스킬"] = PieceVoiceCommand.SkillPrimary,
        ["1번스킬"] = PieceVoiceCommand.SkillPrimary,
        ["보조스킬사용"] = PieceVoiceCommand.SkillSecondary,
        ["두번째스킬"] = PieceVoiceCommand.SkillSecondary,
        ["2번스킬"] = PieceVoiceCommand.SkillSecondary,
        ["돌진"] = PieceVoiceCommand.Charge,
        ["돌진해"] = PieceVoiceCommand.Charge,
        ["돌진해줘"] = PieceVoiceCommand.Charge
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
        "왼쪽으로 가",
        "왼쪽으로 돌아",
        "좌회전",
        "오른쪽 회전",
        "오른쪽으로 가",
        "오른쪽으로 돌아",
        "옆으로 돌아",
        "우회전",
        "오른쪽 위로 가",
        "왼쪽 위로 가",
        "오른쪽 아래로 가",
        "왼쪽 아래로 가",
        "주 스킬 사용",
        "첫 번째 스킬",
        "1번 스킬",
        "보조 스킬 사용",
        "두 번째 스킬",
        "2번 스킬",
        "돌진",
        "돌진해",
        "돌진해 줘"
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
