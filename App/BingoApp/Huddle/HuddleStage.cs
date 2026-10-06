using BingoEngine;
using Choreography.StageManager;
using System.Collections.Concurrent;
using Choreography.Transport;
using Choreography.Transport.Iroh;
using BingoApp.Transport;
using Puppeteer;
using Village;

namespace BingoApp.Huddle;

public static class HuddleStage
{
    private static readonly ConcurrentDictionary<PerformerId, IrohTransport> Wires = new();

    private static IrohTransport WireOf(StageV2 journal) => Wires[journal.Id];

    public static StageV2 MountJournal(string who, Action<string> say)
    {
        string dir = Path.Combine(FileSystem.AppDataDirectory, "corrillo-" + who);
        Directory.CreateDirectory(dir);


        StageV2 journal = new StageV2(
            Identity(dir),
            "funcion",
            typeof(Elder).Assembly,
            typeof(BingoGame).Assembly);

        journal.ConfigureStorage(DatabaseType.FileSystem, "path=" + dir);

        IrohTransport wire = IrohWire.ForStage(journal.Id, dir, new CommunityLeader.JournalVoice(say), say);
        journal.ConfigureTransport(wire);
        Wires[journal.Id] = wire;

        say(Texts.T("corrillo.diario-en", ("carpeta", dir)));

        return journal;
    }

    private static PerformerId Identity(string dir)
    {
        string path = Path.Combine(dir, "identity.txt");

        if (File.Exists(path))
            return new PerformerId(Guid.ParseExact(File.ReadAllText(path).Trim(), "N"));

        PerformerId fresh = PerformerId.New();
        File.WriteAllText(path, fresh.ToString());
        return fresh;
    }

    public sealed class OpenDoor
    {
        public HuddlePass Pass { get; init; }

        public Task<IStageChannel> Coordination { get; init; }

        public Task<IStageChannel> Replication { get; init; }

        public Task<IStageChannel> Commands { get; init; }
    }

    public static async Task<OpenDoor> OpenTheDoorAsync(
        StageV2 journal, string myAddress, CancellationToken life, Action<string> say)
    {
        await WireOf(journal).StartAsync(life);
        ConnectionInvitation warmUp = await journal.CreateInvitationAsync(ChannelPurpose.Coordination);
        _ = journal.WaitForConnectionAsync(warmUp, life);
        await Task.Delay(600, life);
        say(Texts.T("corrillo.listener-caliente"));

        ConnectionInvitation forCoordination =
            await journal.CreateInvitationAsync(ChannelPurpose.Coordination);
        ConnectionInvitation forReplication =
            await journal.CreateInvitationAsync(ChannelPurpose.Replication);
        ConnectionInvitation forCommands =
            await journal.CreateInvitationAsync(ChannelPurpose.Command);

        Task<IStageChannel> waitCoordination = journal.WaitForConnectionAsync(forCoordination, life);
        Task<IStageChannel> waitReplication = journal.WaitForConnectionAsync(forReplication, life);
        Task<IStageChannel> waitCommands = journal.WaitForConnectionAsync(forCommands, life);

        say(Texts.T("corrillo.invitaciones"));

        return new OpenDoor
        {
            Pass = new HuddlePass
            {
                CommunityLeaderTicket = WireOf(journal).LocalTicket,
                CommunityLeaderEndpointId = WireOf(journal).LocalEndpointId,
                InvitedBy = forCoordination.InviterId.ToString(),
                CoordinationAddress = forCoordination.Address,
                ReplicationAddress = forReplication.Address,
                CommandAddress = forCommands.Address,
            },
            Coordination = waitCoordination,
            Replication = waitReplication,
            Commands = waitCommands,
        };
    }

    public static async Task WelcomeCollaboratorAsync(
        StageV2 journal, OpenDoor door, Action<string> say)
    {
        IStageChannel coordination = await door.Coordination;
        await journal.JoinCoordination(coordination.RemotePerformerId, coordination);
        say(Texts.T("corrillo.vendedor-bus"));

        IStageChannel replication = await door.Replication;
        IStageChannel command = await door.Commands;

        await journal.AcceptCastConnection(replication.RemotePerformerId, replication, command);
        say(Texts.T("corrillo.vendedor-entro"));

        await journal.SendCatchUpAsync(replication.RemotePerformerId, 0);
        say(Texts.T("corrillo.mande-diario"));
    }

    public static async Task AttachToCommunityLeaderAsync(
        StageV2 journal, HuddlePass pass, CancellationToken life, Action<string> say)
    {
        await WireOf(journal).StartAsync(life);
        say(Texts.T("corrillo.voy-con", ("organizador", IrohWire.Short(pass.CommunityLeaderEndpointId))));

        PerformerId communityLeader = new PerformerId(Guid.Parse(pass.InvitedBy));

        IStageChannel coordination = await journal.AcceptInvitationAsync(
            new ConnectionInvitation(
                communityLeader, ChannelPurpose.Coordination, pass.CoordinationAddress));

        await journal.JoinCoordination(communityLeader, coordination);
        say(Texts.T("corrillo.entre-bus"));

        IStageChannel replication = await journal.AcceptInvitationAsync(
            new ConnectionInvitation(
                communityLeader, ChannelPurpose.Replication, pass.ReplicationAddress));
        IStageChannel command = await journal.AcceptInvitationAsync(
            new ConnectionInvitation(communityLeader, ChannelPurpose.Command, pass.CommandAddress));

        await journal.ConnectToDirector(communityLeader, replication, command);
        say(Texts.T("corrillo.pegado"));
    }

    public static async Task<bool> WaitToReach(
        StageV2 journal, long entry, TimeSpan howLong, CancellationToken life)
    {
        long deadline = Environment.TickCount64 + (long)howLong.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (journal.CurrentEntryId >= entry) return true;
            await Task.Delay(250, life);
        }

        return false;
    }
}
