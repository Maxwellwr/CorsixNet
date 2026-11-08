using System.Globalization;

namespace CorsixNet.Client.Maps;

/// <summary>Money as the player reads it, such as $40,000 or -$1,200.</summary>
internal static class MoneyText
{
    public static string Format(long amount)
    {
        var digits = Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture);
        return amount < 0 ? $"-${digits}" : $"${digits}";
    }
}
