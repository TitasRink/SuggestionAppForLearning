namespace SuggestionAppLibrary.DataAccess;

public interface IStatusData
{
    Task CreateStatuses(StatusModel status);
    Task<List<StatusModel>> GetAllStatuses();
}