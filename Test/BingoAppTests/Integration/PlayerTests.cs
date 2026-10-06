using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoAppTests.Integration;

[TestClass]
[TestCategory("Emulator")]
public class PlayerTests : OnEmulator
{
    private const int OrganizerPort = 9301;
    private const int CollaboratorPort = 9302;
    private const int PlayerPort = 9303;

    private const string Seller = "Rosa";
    private const string Buyer = "Local1";

    private sealed record Evening(Cdp Organizer, Phone Player, long Card);

    private Evening APlayerWithTheirCard()
    {
        Phone rosa = Collaborator();
        Phone local1 = Player();

        Cdp organizer = OpenAsOrganizer(OrganizerPort);
        CreateTheBingoAndEnter(organizer);

        organizer.Send("El bingo", "entrust|vende|" + Seller + "|10");
        Note("organizer: " + Organizer.WaitForLog("I cut a code for " + Seller, AWhile));
        string rosasCode = CodeOnScreen(organizer, "El bingo", AWhile);

        FromScratch(rosa);
        rosa.OpenLink(rosasCode);
        Cdp collaborator = rosa.DevTools(CollaboratorPort);
        collaborator.Send("En el bingo", "enter|" + Seller + "|" + rosasCode);
        Note("rosa: " + rosa.WaitForLog("I am " + Seller + ", coming in to", OverTheNetwork));

        string handOver = Organizer.WaitForLog(Seller + " arrived to sell; handing them", OverTheNetwork);
        Note("organizer: " + handOver);
        long card = FirstNumberAfter(handOver, "handing them");

        collaborator.Send("En el bingo", "sell||" + Buyer + "||" + card);
        Note("rosa: " + rosa.WaitForLog("I sold " + card, OverTheNetwork));
        string local1sCode = CodeOnScreen(collaborator, "En el bingo", AWhile);

        FromScratch(local1);
        local1.OpenLink(local1sCode);
        Note("local1: " + local1.WaitForLog("I am " + Buyer + ", in bingo", OverTheNetwork));
        Note("local1: " + local1.WaitForLog("my cards arrived", OverTheNetwork));

        return new Evening(organizer, local1, card);
    }

    [TestMethod]
    public void ThePlayerComesInThroughTheLinkOfTheirPurchase()
    {
        Evening evening = APlayerWithTheirCard();

        string cards = evening.Player.WaitForLog("my cards arrived", AWhile);
        StringAssert.Contains(cards, evening.Card.ToString(), "the card Rosa sold reached the player");
    }

    [TestMethod]
    public void TheBallTheOrganizerCalls_IsMarkedOnThePlayersCard()
    {
        Evening evening = APlayerWithTheirCard();

        Cdp player = evening.Player.DevTools(PlayerPort);
        int ball = (int)(Phone.WaitFor(() => ANumberOnTheCard(player), OverTheNetwork)
            ?? throw new AssertFailedException("the player's card was never drawn"));
        Note("local1: waiting for " + ball + ", which is on the card");
        Assert.AreEqual(0, Marked(player).Length, "nothing is marked before the first call");

        evening.Organizer.Send("El bingo", "call");
        evening.Organizer.Evaluate("Cantar", "document.title");
        evening.Organizer.Send("Cantar", "call|" + ball);
        Note("organizer: " + Organizer.WaitForLog("I called " + ball + " in bingo", AWhile));

        Note("local1: " + evening.Player.WaitForLog(ball + " came out", OverTheNetwork));

        string marked = Phone.WaitFor(() => Marked(player).Contains(ball) ? "yes" : null, AWhile);
        Assert.IsNotNull(marked, ball + " came out but was not marked on the card; marked: "
            + string.Join(", ", Marked(player)));
    }

    private static object ANumberOnTheCard(Cdp player)
    {
        int[] numbers = Numbers(player, "#cells .cell-chip");
        return numbers.Length > 0 ? numbers[0] : null;
    }

    private static int[] Marked(Cdp player) => Numbers(player, "#cells .cell-marked .cell-chip");

    private static int[] Numbers(Cdp player, string selector)
    {
        string json = player.Evaluate("El bingo",
            "JSON.stringify([...document.querySelectorAll('" + selector + "')].map(e => e.textContent.trim()))");

        return JsonSerializer.Deserialize<string[]>(json)
            .Where(text => int.TryParse(text, out _))
            .Select(int.Parse)
            .ToArray();
    }
}
