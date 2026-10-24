namespace RippersCasino.Core.Games;

public static class Cards
{
    public static int Rank(int card) => card % 13 + 1;
    public static int[] Shuffle(IRandomSource random, int decks = 1)
    {
        if (decks is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(decks));
        var cards = Enumerable.Range(0, decks * 52).Select(index => index % 52).ToArray();
        for (var i = cards.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        return cards;
    }
}
