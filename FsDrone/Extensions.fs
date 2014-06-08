module Extensions

type Agent<'a> = MailboxProcessor<'a>
type RC<'a>    = AsyncReplyChannel<'a>
