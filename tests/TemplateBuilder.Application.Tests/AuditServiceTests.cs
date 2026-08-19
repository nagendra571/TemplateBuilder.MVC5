using FluentAssertions;
using NSubstitute;
using TemplateBuilder.Application.Services;
using TemplateBuilder.Domain.Entities;
using TemplateBuilder.Domain.Interfaces;
using Xunit;

namespace TemplateBuilder.Application.Tests;

public class AuditServiceTests
{
    private static (AuditService service, IAuditRepository repo) Create()
    {
        var repo = Substitute.For<IAuditRepository>();
        return (new AuditService(repo), repo);
    }

    [Fact]
    public async Task Record_sets_occurred_at_utc_and_persists()
    {
        var (svc, repo) = Create();
        await svc.RecordAsync("Template", 1, AuditActions.Created, "bob");
        await repo.Received(1).AddAsync(Arg.Is<AuditLog>(a =>
            a.EntityType == "Template" && a.EntityId == 1 &&
            a.Action == AuditActions.Created && a.Actor == "bob" &&
            (DateTime.UtcNow - a.OccurredAt).Duration().TotalSeconds < 10));
    }

    [Fact]
    public async Task Draft_saved_is_throttled_to_once_per_five_minutes()
    {
        var (svc, repo) = Create();
        repo.GetLastOccurrenceAsync("Template", 1, AuditActions.DraftSaved, default)
            .Returns(DateTime.UtcNow.AddMinutes(-2));
        await svc.RecordAsync("Template", 1, AuditActions.DraftSaved, "bob");
        await repo.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task Draft_saved_records_when_stale_enough()
    {
        var (svc, repo) = Create();
        repo.GetLastOccurrenceAsync("Template", 1, AuditActions.DraftSaved, default)
            .Returns(DateTime.UtcNow.AddMinutes(-6));
        await svc.RecordAsync("Template", 1, AuditActions.DraftSaved, "bob");
        await repo.Received(1).AddAsync(Arg.Any<AuditLog>());
    }
}