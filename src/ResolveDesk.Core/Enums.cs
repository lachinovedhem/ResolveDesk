namespace ResolveDesk.Core;

/// <summary>Lifecycle of a support ticket.</summary>
public enum TicketStatus { Open = 0, Assigned = 1, InProgress = 2, WaitingCustomer = 3, Resolved = 4, Closed = 5 }

/// <summary>Priority — drives SLA and ordering.</summary>
public enum TicketPriority { Low = 0, Normal = 1, High = 2, Urgent = 3 }

/// <summary>Where the ticket came from (call center is the primary channel).</summary>
public enum TicketSource { Phone = 0, Email = 1, Chat = 2, Portal = 3, Internal = 4 }

/// <summary>Team roles. Coordinator routes tickets to agents.</summary>
public enum UserRole { Agent = 0, Coordinator = 1, Admin = 2 }

/// <summary>Kind of activity entry on a ticket timeline.</summary>
public enum ActivityKind { Comment = 0, StatusChange = 1, Assignment = 2, Resolution = 3, AiSuggestion = 4 }
