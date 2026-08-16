using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class VoiceChargeBatchValidation
{
    public static void Run()
    {
        ValidateChargeRecognitionContext();
        ValidateStretchedChargeParsing();
        ValidateVoiceChargeEconomy();
        ValidatePieceTraits();
        ValidateRandomCaptureResolution();
        EditorApplication.Exit(0);
    }

    private static void ValidatePieceTraits()
    {
        PieceArchetypeSettings pawn = PieceArchetypeSettings.CreateDefault(
            ChessPieceType.Pawn);
        PieceArchetypeSettings rook = PieceArchetypeSettings.CreateDefault(
            ChessPieceType.Rook);
        PieceArchetypeSettings bishop = PieceArchetypeSettings.CreateDefault(
            ChessPieceType.Bishop);
        PieceArchetypeSettings knight = PieceArchetypeSettings.CreateDefault(
            ChessPieceType.Knight);
        PieceArchetypeSettings queen = PieceArchetypeSettings.CreateDefault(
            ChessPieceType.Queen);

        AssertApproximately(pawn.Mass, 1f);
        AssertApproximately(rook.Mass, 2f);
        AssertApproximately(bishop.Mass, 1.25f);
        AssertApproximately(knight.Mass, 1.5f);
        AssertApproximately(queen.Mass, 1.6f);

        if (rook.Traits.ChargeDistanceMultiplier >= 1f ||
            rook.Traits.ShapeChargePower(0.5f) >= 0.5f ||
            rook.Traits.AttackingImpactMultiplier >= 1f)
        {
            throw new InvalidOperationException(
                "룩의 짧은 충전·느린 충전 성장·낮은 공격 밀침 특성이 누락되었습니다.");
        }

        if (!bishop.Traits.FirstAttackingCollisionOnly ||
            bishop.Traits.AttackingImpactMultiplier <= 1f ||
            !queen.Traits.FirstAttackingCollisionOnly ||
            queen.Traits.AttackingImpactMultiplier <= 1f ||
            queen.Traits.AttackingImpactMultiplier >=
            bishop.Traits.AttackingImpactMultiplier)
        {
            throw new InvalidOperationException(
                "비숍 또는 퀸의 첫 공격 충돌 특성이 올바르지 않습니다.");
        }

        if (!knight.Traits.IgnoreFriendlyPieceCollisions ||
            pawn.Traits.IgnoreFriendlyPieceCollisions)
        {
            throw new InvalidOperationException(
                "나이트의 아군 기물 통과 특성이 올바르지 않습니다.");
        }

        TemporaryPieceTraitModifiers temporary = new(
            expiresAtServerTime: 10d,
            massMultiplier: 2f,
            attackingImpactMultiplier: 1.5f,
            addedFlags: PieceTraitFlags.IgnoreFriendlyPieceCollisions);
        ResolvedPieceTraits active = new(pawn.Traits, temporary, 5d);
        ResolvedPieceTraits expired = new(pawn.Traits, temporary, 11d);
        AssertApproximately(active.MassMultiplier, 2f);
        AssertApproximately(active.AttackingImpactMultiplier, 1.5f);
        AssertApproximately(expired.MassMultiplier, 1f);

        if (!active.IgnoreFriendlyPieceCollisions ||
            expired.IgnoreFriendlyPieceCollisions)
        {
            throw new InvalidOperationException(
                "시간 제한 기물 특성의 적용 또는 만료가 올바르지 않습니다.");
        }

        GameModeConfiguration dashboard = AssetDatabase.LoadAssetAtPath<
            GameModeConfiguration>(
            "Assets/_Project/Settings/DefaultGameMode.asset");

        if (dashboard == null)
        {
            throw new InvalidOperationException(
                "DefaultGameMode 기물 특성 설정을 불러오지 못했습니다.");
        }

        AssertApproximately(dashboard.GetPiece(ChessPieceType.Pawn).Mass, 1f);
        AssertApproximately(dashboard.GetPiece(ChessPieceType.Rook).Mass, 2f);
        AssertApproximately(
            dashboard.GetPiece(ChessPieceType.Rook).Traits.ChargeDistanceMultiplier,
            0.7f);
        AssertApproximately(
            dashboard.GetPiece(ChessPieceType.Bishop).Traits
                .AttackingImpactMultiplier,
            1.55f);

        if (!dashboard.GetPiece(ChessPieceType.Knight).Traits
                .IgnoreFriendlyPieceCollisions ||
            !dashboard.GetPiece(ChessPieceType.Queen).Traits
                .FirstAttackingCollisionOnly)
        {
            throw new InvalidOperationException(
                "DefaultGameMode 에셋에 기물별 특성 값이 저장되지 않았습니다.");
        }

        ValidatePieceTraitCollisions(dashboard);
    }

    private static void ValidatePieceTraitCollisions(
        GameModeConfiguration dashboard)
    {
        GameObject gameObject = new("Piece Trait Collision Validation");

        try
        {
            NetworkChessGame game = gameObject.AddComponent<NetworkChessGame>();
            SetPrivateField(game, "gameMode", dashboard);
            MethodInfo resolve = typeof(NetworkChessGame).GetMethod(
                "ResolvePieceCollisions",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (resolve == null)
            {
                throw new MissingMethodException(
                    nameof(NetworkChessGame),
                    "ResolvePieceCollisions");
            }

            NetworkChessPieceState pawnTarget = ResolveTestCollision(
                game,
                resolve,
                ChessPieceType.Pawn,
                ChessPieceType.Pawn,
                PlayerTeam.White,
                PlayerTeam.Black,
                firstImpactAvailable: false,
                out _);
            NetworkChessPieceState rookTarget = ResolveTestCollision(
                game,
                resolve,
                ChessPieceType.Rook,
                ChessPieceType.Pawn,
                PlayerTeam.White,
                PlayerTeam.Black,
                firstImpactAvailable: false,
                out _);
            NetworkChessPieceState bishopTarget = ResolveTestCollision(
                game,
                resolve,
                ChessPieceType.Bishop,
                ChessPieceType.Pawn,
                PlayerTeam.White,
                PlayerTeam.Black,
                firstImpactAvailable: true,
                out NetworkChessPieceState bishopAfterFirstImpact);
            NetworkChessPieceState bishopSecondTarget = ResolveTestCollision(
                game,
                resolve,
                ChessPieceType.Bishop,
                ChessPieceType.Pawn,
                PlayerTeam.White,
                PlayerTeam.Black,
                firstImpactAvailable: false,
                out _);

            if (rookTarget.KnockbackFileVelocity >=
                    pawnTarget.KnockbackFileVelocity ||
                bishopTarget.KnockbackFileVelocity <=
                    pawnTarget.KnockbackFileVelocity ||
                bishopTarget.KnockbackFileVelocity <=
                    bishopSecondTarget.KnockbackFileVelocity ||
                bishopAfterFirstImpact.FirstAttackingCollisionAvailable)
            {
                throw new InvalidOperationException(
                    "룩의 낮은 밀침 또는 비숍의 첫 충돌 보너스가 실제 충돌 계산에 적용되지 않습니다.");
            }

            NetworkChessPieceState friendlyKnightTarget = ResolveTestCollision(
                game,
                resolve,
                ChessPieceType.Knight,
                ChessPieceType.Pawn,
                PlayerTeam.White,
                PlayerTeam.White,
                firstImpactAvailable: false,
                out NetworkChessPieceState friendlyKnight);

            AssertApproximately(friendlyKnight.BoardFile, 0f);
            AssertApproximately(friendlyKnightTarget.BoardFile, 0.67f);
            AssertApproximately(friendlyKnightTarget.KnockbackFileVelocity, 0f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    private static NetworkChessPieceState ResolveTestCollision(
        NetworkChessGame game,
        MethodInfo resolve,
        ChessPieceType attackerType,
        ChessPieceType targetType,
        PlayerTeam attackerTeam,
        PlayerTeam targetTeam,
        bool firstImpactAvailable,
        out NetworkChessPieceState resolvedAttacker)
    {
        NetworkChessPieceState attacker = new(
            1,
            attackerTeam,
            attackerType,
            0f,
            0f)
        {
            KnockbackFileVelocity = 1f,
            FirstAttackingCollisionAvailable = firstImpactAvailable
        };
        NetworkChessPieceState target = new(
            2,
            targetTeam,
            targetType,
            0.67f,
            0f);
        List<NetworkChessPieceState> pieces = new() { attacker, target };
        List<Vector2> commandedVelocities = new()
        {
            Vector2.zero,
            Vector2.zero
        };
        resolve.Invoke(game, new object[] { pieces, commandedVelocities });
        resolvedAttacker = pieces[0];
        return pieces[1];
    }

    private static void ValidateChargeRecognitionContext()
    {
        HashSet<string> expectedPhrases = new()
        {
            "돌진",
            "돌진해",
            "돌진해 줘",
            "공격",
            "공격해",
            "공격해 줘"
        };

        if (KoreanVoiceCommandParser.ChargePhraseHints.Count != expectedPhrases.Count ||
            !expectedPhrases.SetEquals(KoreanVoiceCommandParser.ChargePhraseHints))
        {
            throw new InvalidOperationException(
                "Azure 돌진 전용 인식 문맥에 허용되지 않은 명령이 포함되어 있습니다.");
        }
    }

    private static void ValidateStretchedChargeParsing()
    {
        string[] acceptedExamples =
        {
            "돌진",
            "돌지이이이인",
            "도오올지이인",
            "돌ㄹㄹㄹ진ㄴㄴㄴ",
            "공격",
            "공격해",
            "공격해 줘",
            "누진",
            "부진",
            "진진",
            "전진",
            "후진"
        };

        foreach (string example in acceptedExamples)
        {
            KoreanVoiceParseResult result = KoreanVoiceCommandParser.Parse(example);

            if (!result.Accepted || result.Command != PieceVoiceCommand.Charge)
            {
                throw new InvalidOperationException(
                    $"늘인 돌진 발음 파싱 실패: {example} ({result.Score:F3})");
            }
        }

        string[] rejectedExamples =
        {
            "안녕하세요",
            "도전",
            "멈춰",
            "왼쪽으로 가"
        };

        foreach (string example in rejectedExamples)
        {
            KoreanVoiceParseResult result = KoreanVoiceCommandParser.Parse(example);

            if (result.Accepted && result.Command == PieceVoiceCommand.Charge)
            {
                throw new InvalidOperationException(
                    $"돌진 오탐지: {example} ({result.Score:F3})");
            }
        }
    }

    private static void ValidateVoiceChargeEconomy()
    {
        CommandEconomySettings defaults = new();
        AssertApproximately(defaults.GetVoiceChargeCost(0f), 1f);

        CommandEconomySettings settings = new();
        SetPrivateField(settings, "costConsumptionVersion",
            CostConsumptionVersion.VoiceDurationCharge);
        SetPrivateField(settings, "voiceChargeCostStep", 0.05f);
        SetPrivateField(settings, "voiceChargeSecondsPerCostStep", 0.05f);
        SetPrivateField(settings, "voiceChargeMaximumDurationSeconds", 3f);
        SetPrivateField(settings, "chargeCost", 2f);
        SetPrivateField(settings, "voiceChargeMinimumDistanceInSquares", 0.05f);
        SetPrivateField(settings, "voiceChargeMaximumDistanceInSquares", 8f);
        SetPrivateField(
            settings,
            "voiceChargeMaximumInitialLoudnessDistanceInSquares",
            1f);
        SetPrivateField(settings, "voiceChargeLoudnessExponent", 1.75f);
        SetPrivateField(settings, "voiceChargeDurationWeight", 0.2f);
        SetPrivateField(settings, "voiceChargeDurationExponent", 0.6f);
        SetPrivateField(settings, "voiceChargeLoudnessWeight", 0.8f);

        AssertApproximately(settings.GetVoiceChargeCost(0f), 2f);
        AssertApproximately(settings.GetVoiceChargeCost(0.01f), 2f);
        AssertApproximately(settings.GetVoiceChargeCost(1.23f), 2f);
        AssertApproximately(settings.GetVoiceChargeCost(2.23f), 2.25f);
        AssertApproximately(settings.GetVoiceChargeCost(30f), 3f);
        AssertApproximately(settings.GetVoiceChargeCost(2.23f, 2), 4.5f);
        AssertApproximately(settings.GetVoiceChargeCost(30f, 3), 5f);

        float weak = settings.GetVoiceChargePower(0.2f, 0.1f, 0.2f);
        float strong = settings.GetVoiceChargePower(2.5f, 0.9f, 0.95f);
        float pronunciationOnly = settings.GetVoiceChargePower(0f, 0f, 1f);
        float loudnessOnly = settings.GetVoiceChargePower(0f, 1f, 1f);
        float accurate = settings.GetVoiceChargePower(1f, 0.5f, 1f);
        float inaccurate = settings.GetVoiceChargePower(1f, 0.5f, 0f);
        float shortLoud = settings.GetVoiceChargePower(0.1f, 1f, 1f);
        float mediumLoud = settings.GetVoiceChargePower(1f, 1f, 1f);
        float longLoud = settings.GetVoiceChargePower(2f, 1f, 1f);
        float linearHalfSecond = 0.5f / 3f;
        float smallVoice = settings.GetVoiceChargePower(0.5f, 0.3f, 1f);
        float largeVoice = settings.GetVoiceChargePower(0.5f, 0.9f, 1f);

        if (strong <= weak ||
            settings.GetVoiceChargeDistance(strong) <=
            settings.GetVoiceChargeDistance(weak))
        {
            throw new InvalidOperationException(
                "발화 길이·음량·발음 정확도가 돌진 거리에 반영되지 않습니다.");
        }

        AssertApproximately(
            settings.GetVoiceChargeDistance(pronunciationOnly),
            0.05f);
        AssertApproximately(
            settings.GetVoiceChargeDistance(loudnessOnly),
            1.05f);

        if (inaccurate >= accurate ||
            shortLoud >= mediumLoud ||
            mediumLoud >= longLoud ||
            smallVoice >= largeVoice ||
            shortLoud <= 0f ||
            settings.GetVoiceChargePower(0.5f, 1f, 1f) <= linearHalfSecond)
        {
            throw new InvalidOperationException(
                "발음 감점 또는 발화 시간에 따른 연속 충전이 올바르지 않습니다.");
        }
    }

    private static void ValidateRandomCaptureResolution()
    {
        List<NetworkChessPieceState> pieces = new()
        {
            new NetworkChessPieceState(
                1,
                PlayerTeam.White,
                ChessPieceType.Pawn,
                0.1f,
                0f),
            new NetworkChessPieceState(
                2,
                PlayerTeam.Black,
                ChessPieceType.Pawn,
                0.5f,
                0f),
            new NetworkChessPieceState(
                3,
                PlayerTeam.Black,
                ChessPieceType.Pawn,
                0.75f,
                0f)
        };
        PlayerTeam countWinner = NetworkChessGame.EvaluateRandomCaptureRound(
            pieces,
            Vector2.zero,
            1f,
            0.001f,
            out int whiteCount,
            out int blackCount,
            out _,
            out _);

        if (countWinner != PlayerTeam.Black ||
            whiteCount != 1 ||
            blackCount != 2)
        {
            throw new InvalidOperationException(
                "랜덤 점령전의 기물 수 우선 판정이 올바르지 않습니다.");
        }

        pieces.RemoveAt(2);
        PlayerTeam distanceWinner = NetworkChessGame.EvaluateRandomCaptureRound(
            pieces,
            Vector2.zero,
            1f,
            0.001f,
            out whiteCount,
            out blackCount,
            out float whiteDistance,
            out float blackDistance);

        if (distanceWinner != PlayerTeam.White ||
            whiteCount != blackCount ||
            whiteDistance >= blackDistance)
        {
            throw new InvalidOperationException(
                "랜덤 점령전의 중심 거리 동률 판정이 올바르지 않습니다.");
        }

        pieces.Clear();
        PlayerTeam emptyWinner = NetworkChessGame.EvaluateRandomCaptureRound(
            pieces,
            Vector2.zero,
            1f,
            0.001f,
            out _,
            out _,
            out _,
            out _);

        if (emptyWinner != PlayerTeam.Unassigned)
        {
            throw new InvalidOperationException(
                "빈 점령 원은 무득점이어야 합니다.");
        }

        pieces.Add(new NetworkChessPieceState(
            4,
            PlayerTeam.White,
            ChessPieceType.King,
            0.25f,
            0f));
        PlayerTeam kingWinner = NetworkChessGame.EvaluateRandomCaptureRound(
            pieces,
            Vector2.zero,
            1f,
            0.001f,
            out whiteCount,
            out blackCount,
            out _,
            out _);

        if (kingWinner != PlayerTeam.White ||
            whiteCount != 1 ||
            blackCount != 0)
        {
            throw new InvalidOperationException(
                "랜덤 점령전에서 킹이 하나의 기물로 판정되지 않습니다.");
        }
    }

    private static void SetPrivateField(
        object target,
        string fieldName,
        object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null)
        {
            throw new MissingFieldException(target.GetType().Name, fieldName);
        }

        field.SetValue(target, value);
    }

    private static void AssertApproximately(float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new InvalidOperationException(
                $"수치 계산 불일치: 예상 {expected}, 실제 {actual}");
        }
    }
}
