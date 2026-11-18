using BackThread.Media;
using System.Text.Json;
var media=new MediaSessions();await media.InitializeAsync();var before=await media.SnapshotAsync();
bool requested=args.Length==1&&args[0]=="--next";
bool accepted=false;
if(requested&&before is not null){accepted=await media.CommandAsync("next",source:before.Source);await Task.Delay(4000);}
var after=await media.SnapshotAsync();
Console.WriteLine(JsonSerializer.Serialize(new{requested,accepted,source=before?.Source,canNext=before?.CanNext,canSeek=before?.CanSeek,state=before?.State,trackChanged=before?.Title!=after?.Title,positionBefore=before?.PositionSeconds,positionAfter=after?.PositionSeconds}));
return 0;
