var sagaBuilder = new SagaBuilder("MySaga");

sagaBuilder.Activity<string>("Order-Process", async () =>
{
    // Your Service

    return new SagaResult<string>(true, "My-Order-Complete");
}).CompensationActivity<string>("Order-Process", "Compensation-1", (sagaResult) =>
{
    if (!sagaResult.IsSuccess)
    {
        // Your Compensation Service

        Console.WriteLine($"Compensating for Activity1 failure. Original result was: {sagaResult.Results}");
    }

    return Task.CompletedTask;
})
.CompensationActivity<string>("Order-Process", "Compensation-2", (sagaResult) =>
{
    if (!sagaResult.IsSuccess)
    {
        // Your Compensation Service

        Console.WriteLine($"Compensating for Activity2 failure. Original result was: {sagaResult.Results}");
    }

    return Task.CompletedTask;
});

// Saga Name
Console.WriteLine($"Saga Name => {sagaBuilder.SagaName}");

// Execute Saga
await sagaBuilder.ExecuteAsync();

// Get Activity Results

var activityResult = sagaBuilder.ActivityResults.FirstOrDefault(s => s.ActivityName == "Order-Process" && s.SagaResult.IsSuccess == true);
Console.WriteLine(activityResult.SagaResult.Results as string);

public class SagaResult<T>
{
    public SagaResult(bool isSuccess, T results)
    {
        this.IsSuccess = isSuccess;
        this.Results = results;
    }

    public bool IsSuccess { get; }

    public T Results { get; }
}

public class CompensationActivityModel<T>
{
    public string CompensationName { get; set; }
    public Func<SagaResult<T>, Task> Action { get; set; }

    public CompensationActivityModel(string compensationName, Func<SagaResult<T>, Task> action)
    {
        this.CompensationName = compensationName;
        this.Action = action;
    }
}

public interface ISagaActivity
{
    string? ActivityName { get; }
    Func<Task<SagaResult<dynamic>>> Action { get; }
    List<Func<SagaResult<dynamic>, Task>> Compensations { get; }
}

public class SagaActivity<T> : ISagaActivity
{
    public string? ActivityName { get; }

    public Func<Task<SagaResult<T>>> Action { get; }

    public List<CompensationActivityModel<T>> Compensations { get; set; } = new List<CompensationActivityModel<T>>();

    Func<Task<SagaResult<dynamic>>> ISagaActivity.Action => () => Action().ContinueWith(t => new SagaResult<dynamic>(t.Result.IsSuccess, t.Result.Results));

    List<Func<SagaResult<dynamic>, Task>> ISagaActivity.Compensations => Compensations.Select(c => (Func<SagaResult<dynamic>, Task>)(r => c.Action(new SagaResult<T>(r.IsSuccess, (T)r.Results)))).ToList();

    public SagaActivity(string activityName, Func<Task<SagaResult<T>>> action)
    {
        this.ActivityName = activityName;
        this.Action = action;
    }
}

public class ActivityResult
{
    public ActivityResult(string activityName, SagaResult<dynamic> sagaResult)
    {
        this.ActivityName = activityName;
        this.SagaResult = sagaResult;
    }

    public string ActivityName { get; }
    public SagaResult<dynamic> SagaResult { get; }
}

public class SagaBuilder
{
    private List<ISagaActivity> Activities { get; } = new List<ISagaActivity>();

    public SagaBuilder(string sagaName)
    {
        this.SagaName = sagaName;
    }

    public string? SagaName { get; }

    public List<ActivityResult> ActivityResults { get; } = new List<ActivityResult>();

    public SagaBuilder Activity<T>(string activityName, Func<Task<SagaResult<T>>> action)
    {
        Activities.Add(new SagaActivity<T>(activityName, action));
        return this;
    }

    public SagaBuilder CompensationActivity<T>(string activityName, string compensationName, Func<SagaResult<T>, Task> compensation)
    {
        var activity = Activities.OfType<SagaActivity<T>>().FirstOrDefault(e => e.ActivityName == activityName);
        if (activity != null)
        {
            activity.Compensations.Add(new CompensationActivityModel<T>(compensationName, compensation));
        }
        return this;
    }

    public async Task ExecuteAsync()
    {
        foreach (var activity in Activities)
        {
            try
            {
                var result = await activity.Action();

                if (result is not null)
                    ActivityResults.Add(new ActivityResult(activity.ActivityName, result));

                if (!result.IsSuccess && activity.Compensations.Any())
                {
                    foreach (var compensation in activity.Compensations)
                    {
                        await compensation(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Activity {activity.ActivityName} failed with error: {ex.Message}");
                // Handle exception, compensate activities, etc.
                throw;
            }
        }
    }
}