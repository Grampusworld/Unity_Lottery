using System;
using UnityEngine;
using Random = UnityEngine.Random;

// 出票时生成与预抽奖金一致的奖格。结算沿用 LotteryTicket.Prize，手刮和机器只发一次。
public static class TicketNumberRules
{
    public static int[] Generate(int kind, int prize)
    {
        if (kind == 3)
        {
            if (prize != 0 && prize != LotteryEconomy.HeartPairPrize && prize != LotteryEconomy.HeartTriplePrize)
                throw new ArgumentException("HeartMatch prizes must be 0, 200 or 600.");
            int a = Random.Range(1, 10);
            int b = Different(a);
            int c = Different(a, b);
            int[] numbers = { a, prize > 0 ? a : b, prize == LotteryEconomy.HeartTriplePrize ? a : c };
            Shuffle(numbers);
            return numbers;
        }
        if (kind == 4)
        {
            if (prize < 0 || prize > LotteryEconomy.CrossMatchPrize * 4 || prize % LotteryEconomy.CrossMatchPrize != 0)
                throw new ArgumentException("CrossCode prizes must be multiples of CrossMatchPrize, up to four matches.");
            int target = Random.Range(1, 10);
            int[] corners = new int[4];
            for (int i = 0; i < corners.Length; i++)
                corners[i] = i < prize / LotteryEconomy.CrossMatchPrize ? target : Different(target);
            Shuffle(corners);
            return new[] { target, corners[0], corners[1], corners[2], corners[3] };
        }
        if (kind == 5)
        {
            if (prize < 0) throw new ArgumentException("ZigzagRun prizes must be non-negative.");
            // 不放回抽取 1..99，再排序；输票交换首尾，保证至少一处降序。
            int[] numbers = new int[5];
            for (int i = 0; i < numbers.Length; i++)
            {
                int value;
                do { value = Random.Range(1, 100); }
                while (Array.IndexOf(numbers, value, 0, i) >= 0);
                numbers[i] = value;
            }
            Array.Sort(numbers);
            if (prize == 0)
            {
                int swap = numbers[0]; numbers[0] = numbers[4]; numbers[4] = swap;
            }
            return numbers;
        }
        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    private static int Different(int a, int b = 0)
    {
        int value;
        do { value = Random.Range(1, 10); } while (value == a || value == b);
        return value;
    }

    private static void Shuffle(int[] values)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int swap = values[i]; values[i] = values[j]; values[j] = swap;
        }
    }
}
