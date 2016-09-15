﻿#region Dapplo 2016 - GNU Lesser General Public License

// Dapplo - building blocks for .NET applications
// Copyright (C) 2016 Dapplo
// 
// For more information see: http://dapplo.net/
// Dapplo repositories are hosted on GitHub: https://github.com/dapplo
// 
// This file is part of Dapplo.Jira
// 
// Dapplo.Jira is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Dapplo.Jira is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have a copy of the GNU Lesser General Public License
// along with Dapplo.Jira. If not, see <http://www.gnu.org/licenses/lgpl.txt>.be 

#endregion

#region Usings

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Dapplo.HttpExtensions;
#if !_PCL_
using Dapplo.HttpExtensions.Extensions;
using Dapplo.HttpExtensions.OAuth;
#endif
using Dapplo.Jira.Entities;
using Dapplo.Jira.Internal;
using Dapplo.Log.Facade;

#endregion

namespace Dapplo.Jira
{
	/// <summary>
	///     Jira API, using Dapplo.HttpExtensions
	/// </summary>
	public class JiraApi
	{
		private static readonly LogSource Log = new LogSource();

		private string _password;
		private string _user;

		/// <summary>
		///     Store the specific HttpBehaviour, which contains a IHttpSettings and also some additional logic for making a
		///     HttpClient which works with Jira
		/// </summary>
		internal IHttpBehaviour Behaviour { get; }

		/// <summary>
		///     Create the JiraApi object, here the HttpClient is configured
		/// </summary>
		/// <param name="baseUri">Base URL, e.g. https://yourjiraserver</param>
		/// <param name="httpSettings">IHttpSettings or null for default</param>
		public JiraApi(Uri baseUri, IHttpSettings httpSettings = null) : this(baseUri)
		{
			Behaviour = ConfigureBehaviour(new HttpBehaviour(), httpSettings);
		}

#if !_PCL_
		/// <summary>
		///     Create the JiraApi, using OAuth 1 for the communication, here the HttpClient is configured
		/// </summary>
		/// <param name="baseUri">Base URL, e.g. https://yourjiraserver</param>
		/// <param name="jiraOAuthSettings">JiraOAuthSettings</param>
		/// <param name="httpSettings">IHttpSettings or null for default</param>
		public JiraApi(Uri baseUri, JiraOAuthSettings jiraOAuthSettings, IHttpSettings httpSettings = null) : this(baseUri)
		{
			var jiraOAuthUri = JiraBaseUri.AppendSegments("plugins", "servlet", "oauth");

			var oAuthSettings = new OAuth1Settings
			{
				TokenUrl = jiraOAuthUri.AppendSegments("request-token"),
				TokenMethod = HttpMethod.Post,
				AccessTokenUrl = jiraOAuthUri.AppendSegments("access-token"),
				AccessTokenMethod = HttpMethod.Post,
				CheckVerifier = false,
				SignatureType = OAuth1SignatureTypes.RsaSha1,
				Token = jiraOAuthSettings.Token,
				ClientId = jiraOAuthSettings.ConsumerKey,
				CloudServiceName = jiraOAuthSettings.CloudServiceName,
				RsaSha1Provider = jiraOAuthSettings.RsaSha1Provider,
				AuthorizeMode = jiraOAuthSettings.AuthorizeMode,
				AuthorizationUri = jiraOAuthUri.AppendSegments("authorize")
					.ExtendQuery(new Dictionary<string, string>
					{
						{OAuth1Parameters.Token.EnumValueOf(), "{RequestToken}"},
						{OAuth1Parameters.Callback.EnumValueOf(), "{RedirectUrl}"}
					})
			};

			// Configure the OAuth1Settings

			Behaviour = ConfigureBehaviour(OAuth1HttpBehaviourFactory.Create(oAuthSettings), httpSettings);
		}
#endif
		/// <summary>
		/// Constructor for only the Uri, used internally
		/// </summary>
		/// <param name="baseUri"></param>
		private JiraApi(Uri baseUri)
		{
			JiraBaseUri = baseUri;
			JiraRestUri = baseUri.AppendSegments("rest", "api", "2");
			JiraAuthUri = baseUri.AppendSegments("rest", "auth", "1");
			Issue = new IssueApi(this);
			Attachment = new AttachmentApi(this);
		}

		/// <summary>
		/// Helper method to configure the IChangeableHttpBehaviour
		/// </summary>
		/// <param name="behaviour">IChangeableHttpBehaviour</param>
		/// <param name="httpSettings">IHttpSettings</param>
		/// <returns>the behaviour, but configured as IHttpBehaviour </returns>
		private IHttpBehaviour ConfigureBehaviour(IChangeableHttpBehaviour behaviour, IHttpSettings httpSettings = null)
		{
			behaviour.HttpSettings = httpSettings ?? HttpExtensionsGlobals.HttpSettings;
			behaviour.OnHttpRequestMessageCreated = httpMessage =>
			{
				httpMessage?.Headers.TryAddWithoutValidation("X-Atlassian-Token", "no-check");
				if (!string.IsNullOrEmpty(_user) && _password != null)
				{
					httpMessage?.SetBasicAuthorization(_user, _password);
				}
				return httpMessage;
			};
			return behaviour;
		}

		/// <summary>
		///     The base URI for your JIRA server
		/// </summary>
		public Uri JiraBaseUri { get; }

		/// <summary>
		///     The rest URI for your JIRA server
		/// </summary>
		internal Uri JiraRestUri { get; }

		/// <summary>
		///     The base URI for JIRA auth api
		/// </summary>
		internal Uri JiraAuthUri { get; }

		/// <summary>
		///     Set Basic Authentication for the current client
		/// </summary>
		/// <param name="user">username</param>
		/// <param name="password">password</param>
		public void SetBasicAuthentication(string user, string password)
		{
			_user = user;
			_password = password;
		}

		/// <summary>
		/// Issue domain
		/// </summary>
		public IIssueApi Issue { get; }

		/// <summary>
		/// Attachment domain
		/// </summary>
		public IAttachmentApi Attachment { get; }

		/// <summary>
		/// Returns the content, specified by the Urim from the JIRA server.
		/// This is used internally, but can also be used to get e.g. the icon for an issue type.
		/// </summary>
		/// <typeparam name="TResponse"></typeparam>
		/// <param name="contentUri">Uri</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>TResponse</returns>
		public async Task<TResponse> GetUriContentAsync<TResponse>(Uri contentUri, CancellationToken cancellationToken = default(CancellationToken))
			where TResponse : class
		{
			Log.Debug().WriteLine("Retrieving content from {0}", contentUri);

			Behaviour.MakeCurrent();

			var response = await contentUri.GetAsAsync<HttpResponse<TResponse, string>>(cancellationToken).ConfigureAwait(false);
			if (response.HasError)
			{
				throw new Exception($"Status: {response.StatusCode} Message: {response.ErrorResponse}");
			}
			return response.Response;
		}

		/// <summary>
		///     Retrieve the Avatar for the supplied avatarUrls object
		/// </summary>
		/// <typeparam name="TResponse">the type to return the result into. e.g. Bitmap,BitmapSource or MemoryStream</typeparam>
		/// <param name="avatarUrls">AvatarUrls object from User or Myself method, or a project from the projects</param>
		/// <param name="avatarSize">Use one of the AvatarSizes to specify the size you want to have</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>Bitmap,BitmapSource or MemoryStream (etc) depending on TResponse</returns>
		public async Task<TResponse> GetAvatarAsync<TResponse>(AvatarUrls avatarUrls, AvatarSizes avatarSize = AvatarSizes.ExtraLarge,
			CancellationToken cancellationToken = default(CancellationToken))
			where TResponse : class
		{
			var avatarUri = avatarUrls.GetUri(avatarSize);

			Behaviour.MakeCurrent();

			return await GetUriContentAsync<TResponse>(avatarUri, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		///     Get server information
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e3828
		/// </summary>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>ServerInfo</returns>
		public async Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			Log.Debug().WriteLine("Retrieving server information");

			var serverInfoUri = JiraRestUri.AppendSegments("serverInfo");
			Behaviour.MakeCurrent();

			var response = await serverInfoUri.GetAsAsync<HttpResponse<ServerInfo, Error>>(cancellationToken).ConfigureAwait(false);

			if (response.HasError)
			{
				return HandleErrors(response);
			}
			var serverInfo = response.Response;
			Log.Debug().WriteLine("Server title {0}, version {1}, uri {2}, build date {3}, build number {4}, scm info {5}",serverInfo.ServerTitle, serverInfo.Version, serverInfo.BaseUrl, serverInfo.BuildDate, serverInfo.BuildNumber, serverInfo.ScmInfo);
			return HandleErrors(response);
		}

		/// <summary>
		///     Helper method for handling errors in the response, if the response has an error an exception is thrown.
		///     Else the real response is returned.
		/// </summary>
		/// <typeparam name="TResponse">Type for the ok content</typeparam>
		/// <typeparam name="TError">Type for the error content</typeparam>
		/// <param name="response">TResponse</param>
		/// <returns></returns>
		internal TResponse HandleErrors<TResponse, TError>(HttpResponse<TResponse, TError> response) where TResponse : class where TError : Error
		{
			if (!response.HasError)
			{
				return response.Response;
			}
			var message = response.StatusCode.ToString();
			if (response.ErrorResponse.ErrorMessages != null)
			{
				message = string.Join(", ", response.ErrorResponse.ErrorMessages);
			}
			else if (response.ErrorResponse?.Message != null)
			{
				message = response.ErrorResponse?.Message;
			}
			Log.Warn().WriteLine("Http status code: {0}. Response from server: {1}", response.StatusCode, message);
			throw new Exception($"Status: {response.StatusCode} Message: {message}");
		}

		/// <summary>
		///     Update comment
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e1139
		/// </summary>
		/// <param name="issueKey">jira key to which the comment belongs</param>
		/// <param name="comment">Comment to update</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>Attachment</returns>
		public async Task UpdateCommentAsync(string issueKey, Comment comment, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (issueKey == null)
			{
				throw new ArgumentNullException(nameof(issueKey));
			}

			Log.Debug().WriteLine("Updating comment {0} for issue {1}", comment.Id, issueKey);

			Behaviour.MakeCurrent();
			var attachUri = JiraRestUri.AppendSegments("issue", issueKey, "comment", comment.Id);
			await attachUri.PutAsync(comment, cancellationToken).ConfigureAwait(false);
		}

		#region filter

		/// <summary>
		///     Get filter favorites
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e1388
		/// </summary>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>List of filter</returns>
		public async Task<IList<Filter>> GetFavoriteFiltersAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			Log.Debug().WriteLine("Retrieving favorite filters");

			Behaviour.MakeCurrent();
			var filterFavouriteUri = JiraRestUri.AppendSegments("filter", "favourite");

			// Add the configurable expand values, if the value is not null or empty
			if (JiraConfig.ExpandGetFavoriteFilters?.Length > 0)
			{
				filterFavouriteUri = filterFavouriteUri.ExtendQuery("expand", string.Join(",", JiraConfig.ExpandGetFavoriteFilters));
			}

			var response = await filterFavouriteUri.GetAsAsync<HttpResponse<IList<Filter>, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		/// <summary>
		///     Get filter
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e1388
		/// </summary>
		/// <param name="id">filter id</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>Filter</returns>
		public async Task<Filter> GetFilterAsync(long id, CancellationToken cancellationToken = default(CancellationToken))
		{
			Log.Debug().WriteLine("Retrieving filter {0}", id);

			Behaviour.MakeCurrent();
			var filterUri = JiraRestUri.AppendSegments("filter", id);

			// Add the configurable expand values, if the value is not null or empty
			if (JiraConfig.ExpandGetFilter?.Length > 0)
			{
				filterUri = filterUri.ExtendQuery("expand", string.Join(",", JiraConfig.ExpandGetFilter));
			}

			var response = await filterUri.GetAsAsync<HttpResponse<Filter, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		/// <summary>
		///     Delete filter
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e1388
		/// </summary>
		/// <param name="id">filter id</param>
		/// <param name="cancellationToken">CancellationToken</param>
		public async Task DeleteFilterAsync(long id, CancellationToken cancellationToken = default(CancellationToken))
		{
			Log.Debug().WriteLine("Deleting filter {0}", id);

			Behaviour.MakeCurrent();
			var filterUri = JiraRestUri.AppendSegments("filter", id);

			var response = await filterUri.DeleteAsync<HttpResponse<string, Error>>(cancellationToken).ConfigureAwait(false);
			if (response.StatusCode != HttpStatusCode.NoContent)
			{
				throw new Exception(string.Join(", ", response.ErrorResponse.ErrorMessages));
			}
		}

		#endregion

		#region project

		/// <summary>
		///     Get projects information
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e2779
		/// </summary>
		/// <param name="projectKey">key of the project</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>ProjectDetails</returns>
		public async Task<Project> GetProjectAsync(string projectKey, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (projectKey == null)
			{
				throw new ArgumentNullException(nameof(projectKey));
			}

			Log.Debug().WriteLine("Retrieving project {0}", projectKey);

			var projectUri = JiraRestUri.AppendSegments("project", projectKey);

			// Add the configurable expand values, if the value is not null or empty
			if (JiraConfig.ExpandGetProject?.Length > 0)
			{
				projectUri = projectUri.ExtendQuery("expand", string.Join(",", JiraConfig.ExpandGetProject));
			}

			Behaviour.MakeCurrent();
			var response = await projectUri.GetAsAsync<HttpResponse<Project, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		/// <summary>
		///     Get all visible projects
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e2779
		/// </summary>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>list of ProjectDigest</returns>
		public async Task<IList<ProjectDigest>> GetProjectsAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			Log.Debug().WriteLine("Retrieving projects");

			var projectsUri = JiraRestUri.AppendSegments("project");

			// Add the configurable expand values, if the value is not null or empty
			if (JiraConfig.ExpandGetProjects?.Length > 0)
			{
				projectsUri = projectsUri.ExtendQuery("expand", string.Join(",", JiraConfig.ExpandGetProjects));
			}

			Behaviour.MakeCurrent();
			var response = await projectsUri.GetAsAsync<HttpResponse<IList<ProjectDigest>, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		#endregion

		#region user

		/// <summary>
		///     Get user information
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e5339
		/// </summary>
		/// <param name="username"></param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>User</returns>
		public async Task<User> GetUserAsync(string username, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (username == null)
			{
				throw new ArgumentNullException(nameof(username));
			}
			Log.Debug().WriteLine("Retrieving user {0}", username);

			var userUri = JiraRestUri.AppendSegments("user").ExtendQuery("username", username);
			Behaviour.MakeCurrent();

			var response = await userUri.GetAsAsync<HttpResponse<User, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		/// <summary>
		///     Returns a list of users that match the search string.
		///     This resource cannot be accessed anonymously.
		///     See: https://docs.atlassian.com/jira/REST/latest/#api/2/user-findUsers
		/// </summary>
		/// <param name="query">A query string used to search username, name or e-mail address</param>
		/// <param name="startAt"></param>
		/// <param name="maxResults">Maximum number of results returned, default is 20</param>
		/// <param name="includeActive">If true, then active users are included in the results (default true)</param>
		/// <param name="includeInactive">If true, then inactive users are included in the results (default false)</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>SearchResult</returns>
		public async Task<IList<User>> SearchUserAsync(string query, bool includeActive = true, bool includeInactive = false, int startAt = 0,
			int maxResults = 20, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (query == null)
			{
				throw new ArgumentNullException(nameof(query));
			}
			Log.Debug().WriteLine("Search user via {0}", query);

			Behaviour.MakeCurrent();
			var searchUri = JiraRestUri.AppendSegments("user", "search").ExtendQuery(new Dictionary<string, object>
			{
				{
					"username", query
				},
				{
					"includeActive", includeActive
				},
				{
					"includeInactive", includeInactive
				},
				{
					"startAt", startAt
				},
				{
					"maxResults", maxResults
				}
			});

			var response = await searchUri.GetAsAsync<HttpResponse<IList<User>, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		/// <summary>
		///     Get currrent user information
		///     See: https://docs.atlassian.com/jira/REST/latest/#d2e4253
		/// </summary>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>User</returns>
		public async Task<User> WhoAmIAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			Log.Debug().WriteLine("Retieving who I am");

			var myselfUri = JiraRestUri.AppendSegments("myself");
			Behaviour.MakeCurrent();
			var response = await myselfUri.GetAsAsync<HttpResponse<User, Error>>(cancellationToken).ConfigureAwait(false);
			return HandleErrors(response);
		}

		#endregion

		#region Session

		/// <summary>
		///     Starts new session. No additional authorization requered.
		/// </summary>
		/// <remarks>
		///     Please be aware that although cookie-based authentication has many benefits, such as performance (not having to
		///     make multiple authentication calls), the session cookie can expire..
		/// </remarks>
		/// <param name="username">User username</param>
		/// <param name="password">User password</param>
		/// <param name="cancellationToken">CancellationToken</param>
		/// <returns>LoginInfo</returns>
		public async Task<LoginInfo> StartSessionAsync(string username, string password, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (username == null)
			{
				throw new ArgumentNullException(nameof(username));
			}
			if (password == null)
			{
				throw new ArgumentNullException(nameof(password));
			}
			if (!Behaviour.HttpSettings.UseCookies)
			{
				throw new ArgumentException("Cookies need to be enabled", nameof(IHttpSettings.UseCookies));
			}
			Log.Debug().WriteLine("Starting a session for {0}", username);

			var sessionUri = JiraAuthUri.AppendSegments("session");

			Behaviour.MakeCurrent();

			var content = new StringContent($"{{ \"username\": \"{username}\", \"password\": \"{password}\"}}");
			content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

			var response = await sessionUri.PostAsync<HttpResponse<SessionResponse, Error>>(content, cancellationToken);

			HandleErrors(response);

			return response.Response.LoginInfo;
		}

		/// <summary>
		///     Ends session. No additional authorization required.
		/// </summary>
		public async Task EndSessionAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			// Find the cookie to expire
			var sessionCookies = Behaviour.CookieContainer.GetCookies(JiraBaseUri).Cast<Cookie>().ToList();

			Log.Debug().WriteLine("Ending session");

			// check if a cookie was found, if not skip the end session
			if (sessionCookies.Any())
			{
				if (Log.IsDebugEnabled())
				{
					Log.Debug().WriteLine("Found {0} cookies to invalidate", sessionCookies.Count);
					foreach (var sessionCookie in sessionCookies)
					{
						Log.Debug().WriteLine("Found cookie {0} for domain {1} which expires on {2}", sessionCookie.Name, sessionCookie.Domain, sessionCookie.Expires);
					}
				}
				var sessionUri = JiraAuthUri.AppendSegments("session");

				Behaviour.MakeCurrent();
				var response = await sessionUri.DeleteAsync<HttpResponseMessage>(cancellationToken);

				if (response.StatusCode != HttpStatusCode.NoContent)
				{
					Log.Warn().WriteLine("Failed to close jira session. Status code: {0} ", response.StatusCode);
				}
				// Expire the cookie, no mather what the return code was.
				foreach (var sessionCookie in sessionCookies)
				{
					sessionCookie.Expired = true;
				}
				
			}
		}

		#endregion
	}
}