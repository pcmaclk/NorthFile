using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NorthFileUI.Settings;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<FavoriteItem>))]
[JsonSerializable(typeof(WorkspaceSessionSnapshot.SnapshotDto))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
