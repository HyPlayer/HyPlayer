## 2026-05-28 - NeteaseComment creation

### Patterns
- PersonBase is abstract - use NeteaseUser as concrete Sender type in mappers
- CommentBase inherits from ProvidableItemBase (has Name, ActualId, ProviderId, TypeId)
- CommentBase members: Content, SendDate, Sender(PersonBase), LikedCount
- IHasCover requires GetCoverAsync(ImageResourceQualityTag?, CancellationToken) -> Task<ResourceResultBase>
- ResourceResultBase is in PlayCore.Abstraction.Models namespace

### Gotchas
- PersonBase is abstract, cannot be instantiated directly
- Need both Models and Models.Resources usings for ResourceResultBase resolution
- NeteaseTypeIds.Comment (cm) needed to be added to Constants
