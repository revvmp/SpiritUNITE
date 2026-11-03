using SpiritUNITE.Game.Data;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed class ActivitySchedule
{
    private const long ConfigTimeFieldBase = 100;
    private const long MinimumConfigYear = 1970;
    private const long MaximumConfigYear = 9998;
    private const int NoTaskGroup = 0;

    private readonly ScheduledActivity[] activities;
    private readonly ILookup<int, pbTask_Base> tasksByGroup;
    private readonly ILookup<int, pbDrop_Static> dropsById;

    public ActivitySchedule(GameData gameData)
    {
        activities = [.. gameData.ActivityBase.Rows.Select(ToScheduledActivity)];
        tasksByGroup = gameData.TaskBase.Rows.ToLookup(task => task.GroupID);
        dropsById = gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);
        TaskBase = gameData.TaskBase;
    }

    public DatabinTable<pbTask_Base> TaskBase { get; }

    public ScheduledActivity[] FindActive(DateTimeOffset now) =>
        [.. activities.Where(activity => activity.IsValidAt(now))];

    public ActiveActivityTask[] FindActiveTasks(DateTimeOffset now) =>
        [.. FindActive(now)
            .SelectMany(activity => activity.TaskGroupIds
                .SelectMany(groupId => tasksByGroup[groupId])
                .Select(task => new ActiveActivityTask(activity, task)))
            .DistinctBy(activeTask => activeTask.Task.TaskID)];

    public ActiveActivityTask? FindActiveTask(int taskId, DateTimeOffset now) =>
        FindActiveTasks(now).FirstOrDefault(activeTask => activeTask.Task.TaskID == taskId);

    public ActiveActivityTask[] FindActiveTasksInGroup(int groupId, DateTimeOffset now) =>
        [.. FindActiveTasks(now).Where(activeTask => activeTask.Task.GroupID == groupId)];

    public IEnumerable<pbDrop_Static> FindDrops(int sDropId) => dropsById[sDropId];

    public static DateTimeOffset ParseConfigTime(long configTime)
    {
        var (rest, second) = Math.DivRem(configTime, ConfigTimeFieldBase);
        (rest, var minute) = Math.DivRem(rest, ConfigTimeFieldBase);
        (rest, var hour) = Math.DivRem(rest, ConfigTimeFieldBase);
        (rest, var day) = Math.DivRem(rest, ConfigTimeFieldBase);
        var (year, month) = Math.DivRem(rest, ConfigTimeFieldBase);
        if (year is < MinimumConfigYear or > MaximumConfigYear)
        {
            throw new InvalidDataException($"Activity config time {configTime} has year {year}");
        }

        return new DateTimeOffset((int)year, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddMonths((int)month - 1)
            .AddDays(day - 1)
            .AddHours(hour)
            .AddMinutes(minute)
            .AddSeconds(second);
    }

    private static ScheduledActivity ToScheduledActivity(pbActivity_Base row) => new(
        row,
        ParseConfigTime(row.ValidStartTime),
        ParseConfigTime(row.ValidEndTime),
        [.. row.TaskGroupIDList.Prepend(row.ActivityParam).Where(groupId => groupId != NoTaskGroup).Distinct()]);
}

public sealed record ScheduledActivity(
    pbActivity_Base Row,
    DateTimeOffset ValidStart,
    DateTimeOffset ValidEnd,
    int[] TaskGroupIds)
{
    public bool IsValidAt(DateTimeOffset now) => ValidStart <= now && now <= ValidEnd;
}

public sealed record ActiveActivityTask(ScheduledActivity Activity, pbTask_Base Task);
