using Marten;
using Marten.Events.Projections;
using Marten.Events.Aggregation;
using Microsoft.AspNetCore.Mvc;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddMarten(options =>
{
    options.Connection("User ID=root;Password=12345;Host=localhost;Port=5431;Database=test;Connection Lifetime=0;");
    options.Projections.Add<QuestProjection>(ProjectionLifecycle.Inline);

    // Specify that we want to use STJ as our serializer
    options.UseSystemTextJsonForSerialization();

    options.Events.UseMandatoryStreamTypeDeclaration = true;
});

var app = builder.Build();

app.UseDeveloperExceptionPage();

app.MapOpenApi();

app.MapScalarApiReference();

// You can inject the IDocumentStore and open sessions yourself
app.MapGet("/test",
    async ([FromServices] IDocumentStore store) =>
    {
        var questId = Guid.NewGuid();

        await using var session = store.LightweightSession();
        var started = new QuestStarted(questId, "Destroy the One Ring");
        var joined1 = new MembersJoined(questId, 1, "Hobbiton", ["Frodo", "Sam"]);

        // Start a brand new stream and commit the new events as
        // part of a transaction
        session.Events.StartStream<Quest>(questId, started, joined1);

        // Append more events to the same stream
        var joined2 = new MembersJoined(questId, 3, "Buckland", ["Merry", "Pippen"]);
        var joined3 = new MembersJoined(questId, 10, "Bree", ["Aragorn"]);
        var arrived = new ArrivedAtLocation(questId, 15, "Rivendell");
        session.Events.Append(questId, joined2, joined3, arrived);

        // Save the pending changes to db
        await session.SaveChangesAsync();
    });


// You can inject the IDocumentStore and open sessions yourself
app.MapGet("/test-error",
    async ([FromServices] IDocumentStore store) =>
    {
        var questId = Guid.NewGuid();

        await using var session = store.LightweightSession();
        //var started = new QuestStarted(questId, "Destroy the One Ring");
        var joined1 = new MembersJoined(questId, 1, "Hobbiton", ["Frodo", "Sam"]);

        // Start a brand new stream and commit the new events as
        // part of a transaction
        session.Events.Append(questId, joined1);

        //// Append more events to the same stream
        //var joined2 = new MembersJoined(questId, 3, "Buckland", ["Merry", "Pippen"]);
        //var joined3 = new MembersJoined(questId, 10, "Bree", ["Aragorn"]);
        //var arrived = new ArrivedAtLocation(questId, 15, "Rivendell");
        //session.Events.Append(questId, joined2, joined3, arrived);

        // Save the pending changes to db
        await session.SaveChangesAsync();
    });

app.Run();

#region sample_sample-events

public sealed record ArrivedAtLocation(Guid QuestId, int Day, string Location);

public sealed record MembersJoined(Guid QuestId, int Day, string Location, string[] Members);

public sealed record QuestStarted(Guid QuestId, string Name);

public sealed record QuestEnded(Guid QuestId, string Name);

public sealed record MembersDeparted(Guid QuestId, int Day, string Location, string[] Members);

public sealed record MembersEscaped(Guid QuestId, string Location, string[] Members);


#endregion


#region sample_QuestParty

public sealed record QuestParty(Guid Id, List<string> Members)
{
    // These methods take in events and update the QuestParty
    public static QuestParty Create(QuestStarted started) => new(started.QuestId, []);
    public static QuestParty Apply(MembersJoined joined, QuestParty party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static QuestParty Apply(MembersDeparted departed, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static QuestParty Apply(MembersEscaped escaped, QuestParty party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };
}

#endregion

#region sample_Quest
public sealed record Quest(Guid Id, List<string> Members, List<string> Slayed, string Name, bool isFinished);

public sealed class QuestProjection : SingleStreamProjection<Quest>
{
    public static Quest Create(QuestStarted started) => new(started.QuestId, [], [], started.Name, false);
    public static Quest Apply(MembersJoined joined, Quest party) =>
        party with
        {
            Members = party.Members.Union(joined.Members).ToList()
        };

    public static Quest Apply(MembersDeparted departed, Quest party) =>
        party with
        {
            Members = party.Members.Where(x => !departed.Members.Contains(x)).ToList()
        };

    public static Quest Apply(MembersEscaped escaped, Quest party) =>
        party with
        {
            Members = party.Members.Where(x => !escaped.Members.Contains(x)).ToList()
        };

    public static Quest Apply(QuestEnded ended, Quest party) =>
        party with { isFinished = true };

}

#endregion

