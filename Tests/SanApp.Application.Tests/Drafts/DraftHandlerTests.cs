using Moq;
using SanApp.Application.Drafts;

namespace SanApp.Application.Tests.Drafts;

public sealed class DraftHandlerTests
{
    [Fact]
    public async Task CreateHandlerReturnsTheCreatedDraftAndCapability()
    {
        var store = new Mock<IDraftStore>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new CreatedDraft(
            new DraftSnapshot(Guid.NewGuid(), DraftFormTypes.EventCreate, null, "version", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), new DraftFields(null, null, null, null, null, null, null)),
            "single-use-capability");
        store.Setup(drafts => drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken)).ReturnsAsync(expected);

        var result = await new CreateDraftHandler(store.Object).Handle(
            new CreateDraftCommand(DraftFormTypes.EventCreate, null), cancellationToken);

        Assert.Equal(expected, result);
        store.VerifyAll();
    }

    [Fact]
    public async Task GetHandlerReturnsOnlyTheStoreResult()
    {
        var store = new Mock<IDraftStore>(MockBehavior.Strict);
        var id = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        store.Setup(drafts => drafts.GetAsync(id, cancellationToken)).ReturnsAsync((DraftSnapshot?)null);

        var result = await new GetDraftHandler(store.Object).Handle(new GetDraftQuery(id), cancellationToken);

        Assert.Null(result);
        store.VerifyAll();
    }

    [Fact]
    public async Task UpdateHandlerPassesTheConfirmedVersionAndAllowListedFields()
    {
        var store = new Mock<IDraftStore>(MockBehavior.Strict);
        var id = Guid.NewGuid();
        const string version = "confirmed-version";
        var fields = new DraftFields("Training", "Berlin", null, null, null, null, null);
        var expected = new DraftSnapshot(id, DraftFormTypes.EventCreate, null, "next-version", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), fields);
        var cancellationToken = TestContext.Current.CancellationToken;
        store.Setup(drafts => drafts.UpdateAsync(id, version, fields, cancellationToken)).ReturnsAsync(expected);

        var result = await new UpdateDraftHandler(store.Object).Handle(
            new UpdateDraftCommand(id, version, fields), cancellationToken);

        Assert.Equal(expected, result);
        store.VerifyAll();
    }

    [Fact]
    public async Task DeleteAndAcknowledgementHandlersPreserveStoreOutcomes()
    {
        var store = new Mock<IDraftStore>(MockBehavior.Strict);
        var id = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        const string version = "confirmed-version";
        var cancellationToken = TestContext.Current.CancellationToken;
        store.Setup(drafts => drafts.DeleteAsync(id, cancellationToken)).ReturnsAsync(false);
        store.Setup(drafts => drafts.AcknowledgeAsync(id, version, operationId, cancellationToken))
            .ReturnsAsync(new DraftAcknowledgement(true, false));

        var deleted = await new DeleteDraftHandler(store.Object).Handle(new DeleteDraftCommand(id), cancellationToken);
        var acknowledged = await new AcknowledgeDraftHandler(store.Object).Handle(
            new AcknowledgeDraftCommand(id, version, operationId), cancellationToken);

        Assert.False(deleted);
        Assert.Equal(new DraftAcknowledgement(true, false), acknowledged);
        store.VerifyAll();
    }

    [Fact]
    public async Task DraftValidatorsRejectMissingIdentifiersVersionsAndUnsupportedTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var create = await new CreateDraftCommandValidator().ValidateAsync(
            new CreateDraftCommand("unsupported", null), cancellationToken);
        var get = await new GetDraftQueryValidator().ValidateAsync(new GetDraftQuery(Guid.Empty), cancellationToken);
        var update = await new UpdateDraftCommandValidator().ValidateAsync(
            new UpdateDraftCommand(Guid.Empty, "", null!), cancellationToken);
        var delete = await new DeleteDraftCommandValidator().ValidateAsync(new DeleteDraftCommand(Guid.Empty), cancellationToken);
        var acknowledge = await new AcknowledgeDraftCommandValidator().ValidateAsync(
            new AcknowledgeDraftCommand(Guid.Empty, "", Guid.Empty), cancellationToken);

        Assert.False(create.IsValid);
        Assert.False(get.IsValid);
        Assert.False(update.IsValid);
        Assert.False(delete.IsValid);
        Assert.False(acknowledge.IsValid);
    }

    [Fact]
    public async Task HandlerDoesNotHideIdentityStoreFailures()
    {
        var store = new Mock<IDraftStore>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("store unavailable");
        store.Setup(drafts => drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken))
            .ThrowsAsync(failure);

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateDraftHandler(store.Object)
            .Handle(new CreateDraftCommand(DraftFormTypes.EventCreate, null), cancellationToken));

        Assert.Same(failure, observed);
        store.VerifyAll();
    }
}
