namespace CardManagement.Domain.Enums;

public enum PaymentStatus
{
    Created,
    PendingFraudCheck,
    Approved,
    Routing,
    Processing,
    Completed,
    Failed,
    Reversed,
    Disputed,
    ManualReview
}
