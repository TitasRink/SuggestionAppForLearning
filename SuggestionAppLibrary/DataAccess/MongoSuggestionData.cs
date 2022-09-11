using Microsoft.Extensions.Caching.Memory;

namespace SuggestionAppLibrary.DataAccess;

public class MongoSuggestionData : ISuggestionData
{
	private readonly IDbConnection _db;
	private readonly IMemoryCache _cache;
	private readonly IUserData _userData;
	private readonly IMongoCollection<SuggestionModel> _suggestions;
	private const string CacheName = "StatusData";

	public MongoSuggestionData(IDbConnection db, IMemoryCache cache, IUserData userData)
	{
		_db = db;
		_cache = cache;
		_userData = userData;
		_suggestions = db.SuggestionCollection;
	}

	public async Task<List<SuggestionModel>> GetAllSuggestions()
	{
		var output = _cache.Get<List<SuggestionModel>>(CacheName);
		if (output is null)
		{
			var result = await _suggestions.FindAsync(x => x.Archived == false);
			output = result.ToList();

			_cache.Set(CacheName, output, TimeSpan.FromMinutes(1));
		}

		return output;
	}

	public async Task<List<SuggestionModel>> GetAllApprovedSuggestions()
	{
		var output = await GetAllSuggestions();
		return output.Where(x => x.ApprovedForRelease).ToList();
	}

	public async Task<SuggestionModel> GetSugestion(string id)
	{
		var result = await _suggestions.FindAsync(x => x.Id == id);
		return result.FirstOrDefault();
	}

	public async Task<List<SuggestionModel>> GetAllSuggestionsWaitingForApproval()
	{
		var output = await GetAllSuggestions();
		return output.Where(x => x.ApprovedForRelease == false && x.Rejected == false).ToList();
	}

	public async Task UpdateSuggestion(SuggestionModel suggestion)
	{
		await _suggestions.ReplaceOneAsync(x => x.Id == suggestion.Id, suggestion);
		_cache.Remove(CacheName);
	}

	public async Task UpvoteSuggestion(string suggestionId, string userId)
	{
		var client = _db.Client;

		using var session = await client.StartSessionAsync();

		session.StartTransaction();

		try
		{
			var db = client.GetDatabase(_db.DbName);
			var suggestionsInTransactions = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
			var suggestion = (await suggestionsInTransactions.FindAsync(x => x.Id == suggestionId)).First();

			bool isUpvote = suggestion.UserVotes.Add(userId);
			if (isUpvote == false)
			{
				suggestion.UserVotes.Remove(userId);
			}

			await suggestionsInTransactions.ReplaceOneAsync(x => x.Id == suggestionId, suggestion);

			var usersInTransaction = db.GetCollection<UserModel>(_db.UserCollectionName);
			var user = await _userData.GetUser(suggestion.Author.Id);

			if (isUpvote)
			{
				user.VotedOnSuggestions.Add(new BasicSuggestionModel(suggestion));
			}
			else
			{
				var suggestionToRemove = user.VotedOnSuggestions.Where(x => x.Id == suggestionId).First();
				user.VotedOnSuggestions.Remove(new BasicSuggestionModel(suggestion));
			}
			await usersInTransaction.ReplaceOneAsync(x => x.Id == userId, user);

			await session.CommitTransactionAsync();

			_cache.Remove(CacheName);
		}
		catch (Exception e)
		{
			await session.AbortTransactionAsync();
			throw;
		}
	}

	public async Task CreateSuggestion(SuggestionModel suggestion)
	{
		var client = _db.Client;

		using var session = await client.StartSessionAsync();

		session.StartTransaction();

		try
		{
			var db = client.GetDatabase(_db.DbName);
			var suggestionsInTransactions = db.GetCollection<SuggestionModel>(_db.SuggestionCollectionName);
			await suggestionsInTransactions.InsertOneAsync(suggestion);

			var usersInTransection = db.GetCollection<UserModel>(_db.UserCollectionName);
			var user = await _userData.GetUser(suggestion.Author.Id);
			user.AuthoredSuggestions.Add(new BasicSuggestionModel(suggestion));
			await usersInTransection.ReplaceOneAsync(x => x.Id == user.Id, user);

			await session.CommitTransactionAsync();
		}
		catch (Exception e)
		{
			await session.AbortTransactionAsync();
			throw;
		}
	}
}
