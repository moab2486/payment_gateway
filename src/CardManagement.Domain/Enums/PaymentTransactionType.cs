namespace CardManagement.Domain.Enums;

public enum PaymentTransactionType
{
    InterbankTransfer,
    QRPayment,
    BillPayment,
    USSDPayment,
    RecurringDebit,
    BulkPayment,
    CardAuthorization
}
