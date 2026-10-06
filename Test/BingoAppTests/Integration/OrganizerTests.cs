using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BingoAppTests.Integration;

[TestClass]
[TestCategory("Emulator")]
public class OrganizerTests : OnEmulator
{
    private const int DevToolsPort = 9301;

    [TestMethod]
    public void TheOrganizerOpensANewBingo()
    {
        Cdp pages = OpenAsOrganizer(DevToolsPort);

        string born = CreateTheBingoAndEnter(pages);

        StringAssert.Contains(born, "1 round(s), 30 cards");
        Assert.AreEqual("El bingo", pages.Evaluate("El bingo", "document.title"), "after creating it the organizer is inside the bingo");
    }

    [TestMethod]
    public void TheOrganizerCallsABall()
    {
        Cdp pages = OpenAsOrganizer(DevToolsPort);
        CreateTheBingoAndEnter(pages);

        pages.Send("El bingo", "call");
        pages.Evaluate("Cantar", "document.title");

        pages.Send("Cantar", "call|12");

        Note("organizer: " + Organizer.WaitForLog("I called 12 in bingo", AWhile));
    }
}
