namespace MW3.Transport;

/// <summary>Which of <see cref="WireMessage"/>'s optional fields are meaningful.</summary>
public enum WireMessageKind
{
    /// <summary>Client to server: opens the connection, announces the client's protocol version.</summary>
    Hello,

    /// <summary>Server to client, replying to <see cref="Hello"/>: the server's version and its map catalogue, in catalogue order.</summary>
    Welcome,

    /// <summary>Client to server: start a match on a named map, at a given time scale (D-62, D-79).</summary>
    CreateSession,

    /// <summary>Server to client, replying to <see cref="CreateSession"/>: the assigned match id and the initial snapshot.</summary>
    SessionCreated,

    /// <summary>Client to server: submit a command, correlated by an id the client assigns.</summary>
    Command,

    /// <summary>Server to client, replying to a <see cref="Command"/> by its id: accepted, or rejected with a reason.</summary>
    CommandResult,

    /// <summary>Server to client: the events between the client's last-known tick and this one, plus a snapshot hash (D-71).</summary>
    Events,

    /// <summary>Either direction: a clean refusal - a version mismatch, a malformed message, or a rejected session request.</summary>
    Error,
}
