using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public readonly struct KoreanVoiceParseResult
{
    public readonly bool Accepted;
    public readonly PieceVoiceCommand Command;
    public readonly string NormalizedText;
    public readonly string MatchedPhrase;
    public readonly float Score;
    public readonly string Reason;

    public KoreanVoiceParseResult(
        bool accepted,
        PieceVoiceCommand command,
        string normalizedText,
        string matchedPhrase,
        float score,
        string reason)
    {
        Accepted = accepted;
        Command = command;
        NormalizedText = normalizedText;
        MatchedPhrase = matchedPhrase;
        Score = score;
        Reason = reason;
    }
}

/// <summary>
/// Standalone parser used by VoiceTestScene. It does not move a piece or send
/// a network message. After the command vocabulary is verified, the game can
/// explicitly opt in to this parser.
/// </summary>
public static class KoreanVoiceCommandParser
{
    private const float FuzzyAcceptanceScore = 0.72f;

    private readonly struct PhraseDefinition
    {
        public readonly string Phrase;
        public readonly string Comparable;
        public readonly PieceVoiceCommand Command;

        public PhraseDefinition(string phrase, PieceVoiceCommand command)
        {
            Phrase = phrase;
            Comparable = MakeComparable(phrase);
            Command = command;
        }
    }

    private static readonly string[] Fillers =
    {
        "체스말을", "체스말", "기물을", "기물", "말을", "말",
        "지금", "이제", "바로", "한번", "한 번", "조금", "좀",
        "그대로", "쭉"
    };

    private static readonly string[] PoliteEndings =
    {
        "해주시겠어요", "해주실래요", "해주십시오", "해주세요",
        "해줘요", "해줘", "하세요", "해요", "해라",
        "주시겠어요", "주실래요", "주십시오", "주세요",
        "줘요", "줘", "줄래", "줄래요", "세요", "십시오", "해", "요"
    };

    private static readonly PhraseDefinition[] Definitions =
    {
        new("앞으로 가", PieceVoiceCommand.MoveForward),
        new("앞으로 이동", PieceVoiceCommand.MoveForward),
        new("앞쪽으로 가", PieceVoiceCommand.MoveForward),
        new("앞쪽으로 이동", PieceVoiceCommand.MoveForward),
        new("앞으로 쭉 가", PieceVoiceCommand.MoveForward),
        new("계속 앞으로 가", PieceVoiceCommand.MoveForward),
        new("계속 가", PieceVoiceCommand.MoveForward),
        new("전진", PieceVoiceCommand.MoveForward),
        new("직진", PieceVoiceCommand.MoveForward),
        new("앞으로 나아가", PieceVoiceCommand.MoveForward),

        new("뒤로 가", PieceVoiceCommand.MoveBackward),
        new("뒤로 이동", PieceVoiceCommand.MoveBackward),
        new("뒤쪽으로 가", PieceVoiceCommand.MoveBackward),
        new("계속 뒤로 가", PieceVoiceCommand.MoveBackward),
        new("후진", PieceVoiceCommand.MoveBackward),

        new("멈춰", PieceVoiceCommand.Stop),
        new("그만", PieceVoiceCommand.Stop),
        new("정지", PieceVoiceCommand.Stop),
        new("스톱", PieceVoiceCommand.Stop),
        new("이동 중지", PieceVoiceCommand.Stop),
        new("여기서 멈춰", PieceVoiceCommand.Stop),

        new("왼쪽으로 돌아", PieceVoiceCommand.TurnLeft),
        new("왼쪽으로 회전", PieceVoiceCommand.TurnLeft),
        new("왼쪽으로 틀어", PieceVoiceCommand.TurnLeft),
        new("좌회전", PieceVoiceCommand.TurnLeft),
        new("좌측으로 돌아", PieceVoiceCommand.TurnLeft),

        new("오른쪽으로 돌아", PieceVoiceCommand.TurnRight),
        new("오른쪽으로 회전", PieceVoiceCommand.TurnRight),
        new("오른쪽으로 틀어", PieceVoiceCommand.TurnRight),
        new("우회전", PieceVoiceCommand.TurnRight),
        new("우측으로 돌아", PieceVoiceCommand.TurnRight),
        new("옆으로 돌아", PieceVoiceCommand.TurnRight)
    };

    private static readonly string[] SequenceConnectors =
    {
        "그리고 나서", "그 다음에", "그다음에", "그리고", "그 다음", "그다음", "다음에", "다음"
    };

    private static readonly string[] SequenceBoundaryEndings =
    {
        "하고", "고"
    };

    private static readonly PhraseDefinition[] SequenceDefinitions = Definitions
        .OrderByDescending(definition => definition.Comparable.Length)
        .ToArray();

    public static IReadOnlyList<string> PhraseHints { get; } = Definitions
        .Select(definition => definition.Phrase)
        .Distinct()
        .ToArray();

    public static IReadOnlyList<KoreanVoiceParseResult> ParseSequence(
        string recognizedText)
    {
        string normalized = Normalize(recognizedText);
        string comparable = MakeSequenceComparable(recognizedText);
        List<PhraseDefinition> matches = new();

        if (comparable.Length > 0 &&
            TrySegmentExactSequence(
                comparable,
                0,
                matches,
                new HashSet<int>()))
        {
            return matches
                .Select(match => Accepted(
                    match.Command,
                    normalized,
                    match.Phrase,
                    1f,
                    matches.Count > 1
                        ? "연속 명령에서 정확히 분리"
                        : "등록 문장과 정확히 일치"))
                .ToArray();
        }

        KoreanVoiceParseResult single = Parse(recognizedText);
        return single.Accepted
            ? new[] { single }
            : Array.Empty<KoreanVoiceParseResult>();
    }

    public static KoreanVoiceParseResult Parse(string recognizedText)
    {
        string normalized = Normalize(recognizedText);
        string comparable = MakeComparable(recognizedText);

        if (comparable.Length == 0)
        {
            return Rejected(normalized, "인식된 글자가 없습니다.");
        }

        if (ContainsAny(
                normalized,
                "하지마", "하지말",
                "가지마", "가지말",
                "돌지마", "돌지말",
                "멈추지마", "멈추지말",
                "말고"))
        {
            return Rejected(
                normalized,
                "부정 표현이 포함되어 안전하게 명령을 실행하지 않습니다.");
        }

        foreach (PhraseDefinition definition in Definitions)
        {
            if (comparable == definition.Comparable)
            {
                return Accepted(
                    definition.Command,
                    normalized,
                    definition.Phrase,
                    1f,
                    "등록 문장과 정확히 일치");
            }
        }

        if (TryKeywordMatch(comparable, normalized, out KoreanVoiceParseResult keywordResult))
        {
            return keywordResult;
        }

        PhraseDefinition bestDefinition = default;
        float bestScore = 0f;

        foreach (PhraseDefinition definition in Definitions)
        {
            if (HasConflictingDirection(comparable, definition.Command))
            {
                continue;
            }

            float score = GetSimilarity(comparable, definition.Comparable);

            if (score > bestScore)
            {
                bestScore = score;
                bestDefinition = definition;
            }
        }

        if (bestScore >= FuzzyAcceptanceScore)
        {
            return Accepted(
                bestDefinition.Command,
                normalized,
                bestDefinition.Phrase,
                bestScore,
                "등록 문장과 글자 유사도 일치");
        }

        string nearest = bestScore > 0f ? bestDefinition.Phrase : "없음";
        return new KoreanVoiceParseResult(
            false,
            default,
            normalized,
            nearest,
            bestScore,
            "명령을 확정하기에는 유사도가 낮습니다.");
    }

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);

        foreach (char character in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string MakeSequenceComparable(string text)
    {
        string result = MakeComparable(text);

        foreach (string connector in SequenceConnectors)
        {
            result = result.Replace(
                Normalize(connector),
                string.Empty);
        }

        return result;
    }

    private static bool TrySegmentExactSequence(
        string text,
        int index,
        List<PhraseDefinition> matches,
        HashSet<int> failedIndices)
    {
        if (index >= text.Length)
        {
            return matches.Count > 0;
        }

        if (failedIndices.Contains(index))
        {
            return false;
        }

        foreach (PhraseDefinition definition in SequenceDefinitions)
        {
            if (index + definition.Comparable.Length > text.Length ||
                string.CompareOrdinal(
                    text,
                    index,
                    definition.Comparable,
                    0,
                    definition.Comparable.Length) != 0)
            {
                continue;
            }

            matches.Add(definition);
            int nextIndex = index + definition.Comparable.Length;

            if (TrySegmentExactSequence(
                    text,
                    nextIndex,
                    matches,
                    failedIndices))
            {
                return true;
            }

            foreach (string ending in SequenceBoundaryEndings)
            {
                string comparableEnding = Normalize(ending);

                if (nextIndex + comparableEnding.Length > text.Length ||
                    string.CompareOrdinal(
                        text,
                        nextIndex,
                        comparableEnding,
                        0,
                        comparableEnding.Length) != 0)
                {
                    continue;
                }

                if (TrySegmentExactSequence(
                        text,
                        nextIndex + comparableEnding.Length,
                        matches,
                        failedIndices))
                {
                    return true;
                }
            }

            matches.RemoveAt(matches.Count - 1);
        }

        failedIndices.Add(index);
        return false;
    }

    private static string MakeComparable(string text)
    {
        string result = Normalize(text);

        foreach (string filler in Fillers)
        {
            result = result.Replace(Normalize(filler), string.Empty);
        }

        bool endingRemoved;

        do
        {
            endingRemoved = false;

            foreach (string ending in PoliteEndings)
            {
                string normalizedEnding = Normalize(ending);

                if (result.Length <= normalizedEnding.Length ||
                    !result.EndsWith(normalizedEnding, StringComparison.Ordinal))
                {
                    continue;
                }

                result = result.Substring(0, result.Length - normalizedEnding.Length);
                endingRemoved = true;
                break;
            }
        }
        while (endingRemoved);

        return result;
    }

    private static bool TryKeywordMatch(
        string text,
        string normalized,
        out KoreanVoiceParseResult result)
    {
        if (ContainsAny(text, "멈", "정지", "스톱", "그만", "중지"))
        {
            result = Accepted(
                PieceVoiceCommand.Stop,
                normalized,
                "멈춤 계열 표현",
                0.97f,
                "멈춤 핵심어 감지");
            return true;
        }

        bool moveAction = ContainsAny(text, "가", "이동", "나아", "진행", "전진", "직진", "후진");
        bool backwardDirection = ContainsAny(text, "뒤", "후진");

        if (backwardDirection && moveAction)
        {
            result = Accepted(
                PieceVoiceCommand.MoveBackward,
                normalized,
                "뒤로 이동",
                0.94f,
                "뒤 방향 + 이동 핵심어 감지");
            return true;
        }

        bool turnAction = ContainsAny(text, "돌", "회전", "틀", "꺾", "방향전환");

        if (turnAction && ContainsAny(text, "왼쪽", "왼", "좌측", "좌회전"))
        {
            result = Accepted(
                PieceVoiceCommand.TurnLeft,
                normalized,
                "왼쪽 회전",
                0.95f,
                "왼쪽 방향 + 회전 핵심어 감지");
            return true;
        }

        if (turnAction && ContainsAny(text, "오른쪽", "오른", "우측", "우회전"))
        {
            result = Accepted(
                PieceVoiceCommand.TurnRight,
                normalized,
                "오른쪽 회전",
                0.95f,
                "오른쪽 방향 + 회전 핵심어 감지");
            return true;
        }

        if (turnAction && text.Contains("옆", StringComparison.Ordinal))
        {
            result = Accepted(
                PieceVoiceCommand.TurnRight,
                normalized,
                "옆으로 돌아",
                0.86f,
                "방향이 없는 ‘옆’은 오른쪽 회전으로 약속");
            return true;
        }

        bool forwardDirection = ContainsAny(text, "앞", "전진", "직진");

        if (forwardDirection && moveAction)
        {
            result = Accepted(
                PieceVoiceCommand.MoveForward,
                normalized,
                "앞으로 이동",
                0.94f,
                "앞 방향 + 이동 핵심어 감지");
            return true;
        }

        result = default;
        return false;
    }

    private static bool HasConflictingDirection(
        string text,
        PieceVoiceCommand command)
    {
        bool mentionsLeft = ContainsAny(text, "왼쪽", "왼", "좌측", "좌회전");
        bool mentionsRight = ContainsAny(text, "오른쪽", "오른", "우측", "우회전");
        bool mentionsBackward = ContainsAny(text, "뒤", "후진");

        return command switch
        {
            PieceVoiceCommand.MoveForward => mentionsLeft || mentionsRight || mentionsBackward,
            PieceVoiceCommand.MoveBackward => mentionsLeft || mentionsRight,
            PieceVoiceCommand.TurnLeft => mentionsRight || mentionsBackward,
            PieceVoiceCommand.TurnRight => mentionsLeft || mentionsBackward,
            _ => mentionsLeft || mentionsRight || mentionsBackward
        };
    }

    private static float GetSimilarity(string left, string right)
    {
        int maximumLength = Math.Max(left.Length, right.Length);

        if (maximumLength == 0)
        {
            return 1f;
        }

        int distance = GetLevenshteinDistance(left, right);
        return 1f - (float)distance / maximumLength;
    }

    private static int GetLevenshteinDistance(string left, string right)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];

        for (int column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= left.Length; row++)
        {
            current[0] = row;

            for (int column = 1; column <= right.Length; column++)
            {
                int substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        foreach (string value in values)
        {
            if (text.Contains(value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static KoreanVoiceParseResult Accepted(
        PieceVoiceCommand command,
        string normalized,
        string matchedPhrase,
        float score,
        string reason)
    {
        return new KoreanVoiceParseResult(
            true,
            command,
            normalized,
            matchedPhrase,
            score,
            reason);
    }

    private static KoreanVoiceParseResult Rejected(string normalized, string reason)
    {
        return new KoreanVoiceParseResult(
            false,
            default,
            normalized,
            string.Empty,
            0f,
            reason);
    }
}
