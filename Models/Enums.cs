namespace Project_Advanced.Models
{
    public enum WorkOrderStatus
    {
        Open = 0,
        InProgress = 1,
        Completed = 2,
        Invoiced = 3
    }

    public enum WorkOrderItemType
    {
        Part = 0,
        Labor = 1
    }

    public enum InvoiceStatus
    {
        Unpaid = 0,
        PartiallyPaid = 1,
        Paid = 2
    }

    public enum PaymentMethod
    {
        Cash = 0,
        Card = 1,
        Whish = 2
    }

    public enum AppointmentStatus
    {
        Pending = 0,
        Confirmed = 1,
        Completed = 2,
        Cancelled = 3
    }
}
