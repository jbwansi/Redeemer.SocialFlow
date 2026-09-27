
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Domain.Entities;

public class SocialPost
{
	private readonly TimeProvider _timeProvider;

	private SocialPost() : this(TimeProvider.System) { }

	private SocialPost(TimeProvider timeProvider) => _timeProvider = timeProvider;

	public Guid Id { get; private set; }
	public bool IsDeleted { get; private set; }
	public DateTimeOffset? DeletedAt { get; private set; }

	public string Title { get; private set; } = string.Empty;
	public string Content { get; private set; } = string.Empty;

	public SocialPlatform Platform { get; private set; }
	public SocialPostStatus Status { get; private set; }

	public string? CallToAction { get; private set; }
	public string? VisualBrief { get; private set; }
	public string? VisualUrl { get; private set; }

	public DateTimeOffset? ScheduledAt { get; private set; }
	public DateTimeOffset? PublishedAt { get; private set; }

	public DateTimeOffset CreatedAt { get; private set; }
	public DateTimeOffset UpdatedAt { get; private set; }

	public static SocialPost Create(
		string title,
		string? content,
		SocialPlatform platform,
		TimeProvider? timeProvider = null)
	{
		if (string.IsNullOrWhiteSpace(title))
			throw new DomainException(
				"Le titre est obligatoire.");

		if (!Enum.IsDefined(platform))
			throw new DomainException(
				"La plateforme est invalide.");

		var clock = timeProvider ?? TimeProvider.System;
		var now = clock.GetUtcNow();

		return new SocialPost(clock)
		{
			Id = Guid.NewGuid(),
			Title = title.Trim(),
			Content = content?.Trim() ?? string.Empty,
			Platform = platform,
			Status = SocialPostStatus.Draft,
			CreatedAt = now,
			UpdatedAt = now
		};
	}

	public void Update(
		string title,
		string? content,
		string? callToAction,
		string? visualBrief,
		string? visualUrl = null)
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.Draft &&
			Status != SocialPostStatus.Rejected)
			throw new DomainException(
				"Cette publication ne peut plus être modifiée.");

		if (string.IsNullOrWhiteSpace(title))
			throw new DomainException(
				"Le titre est obligatoire.");

		Title = title.Trim();
		Content = content?.Trim() ?? string.Empty;
		CallToAction = callToAction?.Trim();
		VisualBrief = visualBrief?.Trim();
		VisualUrl = visualUrl?.Trim();

		UpdatedAt = _timeProvider.GetUtcNow();
	}

	public void SubmitForReview()
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.Draft &&
			Status != SocialPostStatus.Rejected)
			throw new DomainException(
				"La publication ne peut pas être soumise.");

		if (string.IsNullOrWhiteSpace(Content))
			throw new DomainException(
				"Le contenu est obligatoire.");

		Status = SocialPostStatus.ReadyForReview;
		UpdatedAt = _timeProvider.GetUtcNow();
	}

	public void Approve()
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.ReadyForReview)
			throw new DomainException(
				"Seule une publication en attente peut être approuvée.");

		Status = SocialPostStatus.Approved;
		UpdatedAt = _timeProvider.GetUtcNow();
	}

	public void Reject()
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.ReadyForReview)
			throw new DomainException(
				"Seule une publication en attente peut être rejetée.");

		Status = SocialPostStatus.Rejected;
		UpdatedAt = _timeProvider.GetUtcNow();
	}

	public void Schedule(DateTimeOffset date)
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.Approved)
			throw new DomainException(
				"La publication doit être approuvée.");

		var now = _timeProvider.GetUtcNow();
		if (date <= now)
			throw new DomainException(
				"La date doit être dans le futur.");

		ScheduledAt = date;
		Status = SocialPostStatus.Scheduled;
		UpdatedAt = now;
	}

	public void MarkAsPublished()
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.Scheduled)
			throw new DomainException(
				"La publication doit être programmée.");

		var now = _timeProvider.GetUtcNow();
		PublishedAt = now;
		Status = SocialPostStatus.Published;
		UpdatedAt = now;
	}

	public void MarkAsFailed()
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.Scheduled)
			throw new DomainException(
				"Seule une publication programmée peut échouer.");

		Status = SocialPostStatus.Failed;
		UpdatedAt = _timeProvider.GetUtcNow();
	}

	public void Cancel()
	{
		EnsureNotDeleted();
		if (Status != SocialPostStatus.Scheduled)
			throw new DomainException(
				"Seule une publication programmée peut être annulée.");

		Status = SocialPostStatus.Cancelled;
		ScheduledAt = null;
		UpdatedAt = _timeProvider.GetUtcNow();
	}

	public void EnsureCanDelete()
	{
		EnsureNotDeleted();
		if (Status is not (SocialPostStatus.Draft or SocialPostStatus.Rejected or SocialPostStatus.Cancelled))
			throw new DomainException("Seule une publication brouillon, rejetée ou annulée peut être supprimée.");
	}

	public void SoftDelete()
	{
		EnsureCanDelete();
		var now = _timeProvider.GetUtcNow();
		IsDeleted = true;
		DeletedAt = now;
		UpdatedAt = now;
	}

	private void EnsureNotDeleted()
	{
		if (IsDeleted) throw new DomainException("Cette publication a été supprimée.");
	}
}
