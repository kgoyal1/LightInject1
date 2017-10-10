// <copyright file="EPPSAuthenticationFilter.cs" company="Ellie Mae">
// Copyright (c) Ellie Mae. All rights reserved.
// </copyright>

namespace Elli.EPPS.Service.Filters
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Security.Claims;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using System.Web.Http.Controllers;
    using System.Web.Http.Filters;
    using System.Web.Http.Results;
    using System.Web.Http.Routing;
    using Elli.EPPS.Integration.Common;
    using Models.Errors;    
    using Elli.Identity;
    using Identity.Key;
    using Logging;
    using Models.Security;
    using Models;
    using Elli.EPPS.Integration.Settings;

    ///<summary>
    /// http://www.asp.net/web-api/overview/security/authentication-filters
    /// </summary>
    public class EPPSAuthenticationFilter : Attribute, IAuthenticationFilter
    {
        private const string EPPSUrn = "urn:elli:service:epps";
        private const string EbsUrn = "urn:elli:service:ebs";
        private const string IdentityPlatformUrn = "urn:elli:service:ids";

        public EPPSAuthenticationFilter(ILogger logger, IApplicationSettings authenticationSecrets)            
        {
            this.Logger = logger;         

            if (authenticationSecrets?.AuthenticationSecrets == null ||
                authenticationSecrets.AuthenticationSecrets.Count() == 0 ||
                authenticationSecrets.AuthenticationSecrets.Any(item => string.IsNullOrWhiteSpace(item)))
            {
                throw new ConfigurationErrorsException("Required IAuthenticationSecrets value not provided.");
            }

            int keyIndex = 0;
            this.Keys = new byte[authenticationSecrets.AuthenticationSecrets.Count()][];
            foreach (var authenticationSecret in authenticationSecrets.AuthenticationSecrets)
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(authenticationSecrets.AuthenticationSecrets.ElementAt(keyIndex));
                if (keyBytes.Length < 64)
                {
                    Array.Resize(ref keyBytes, 64);
                }

                this.Keys[keyIndex] = keyBytes;

                keyIndex++;
            }
        }

        /// <summary>
        /// Gets or sets the logger for diagnostics.
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Gets wether or not multiple filters of this type are allowed.
        /// Only one is - will return false.
        /// </summary>
        public bool AllowMultiple
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// An array of potentially valid keys to try.
        /// </summary>
        public byte[][] Keys { get; set; }

        /// <summary>
        /// Will perform authentication for the request. Populating the .net principal.
        /// </summary>
        /// <param name="context">WebApi authentication context.</param>
        /// <param name="cancellationToken">Used to manage asynchronous cancellation.</param>
        /// <returns>Task to manage asynchronous execution</returns>
        public async Task AuthenticateAsync(HttpAuthenticationContext context, CancellationToken cancellationToken)
        {
            try
            {
                var authorization = context.Request.Headers.Authorization;
                if (authorization != null)
                {
                    // temporary solution
                    if (authorization.Scheme.Equals("bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = authorization.Parameter;
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            var jwtHandlerExceptions = new List<Exception>();
                            for (int keyIndex = 0; keyIndex < this.Keys.Length; keyIndex++)
                            {
                                ClaimsIdentity identity = null;
                                byte[] key = this.Keys[keyIndex];
                                var elliJwtHandler = new ElliOapiJwtTokenProvider(new HmacKeyFactory(key));
                                const string roleType = "roles";

                                try
                                {
                                    var claims = elliJwtHandler.ValidateToken(token, EPPSUrn);
                                    if (claims != null)
                                    {
                                        var flattenedClaims = this.FlattenClaims(claims, roleType);
                                        identity = new ClaimsIdentity(
                                            flattenedClaims,
                                            AuthenticationTypes.Signature,
                                            "sub",
                                            roleType);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    jwtHandlerExceptions.Add(ex);
                                }

                                if (identity != null)
                                {
                                    //Check to make sure jwt identity is either application or enterprise.
                                    if(IsApplicationIdentity(identity) || IsEnterpriseIdentity(identity))
                                    {                                        
                                        var principal = new ClaimsPrincipal(identity);
                                        context.Principal = principal;

                                        EPPSToken eppsToken = new EPPSToken();
                                        eppsToken.UUID = identity.Claims.FirstOrDefault(claim => claim.Type.Equals(ElliJwtConstants.ClaimTypeUserIdentity, StringComparison.OrdinalIgnoreCase))?.Value;
                                        context.Request.Properties.Add("EppsToken", eppsToken);
                                        return;
                                        
                                    }
                                    else
                                    {
                                        jwtHandlerExceptions.Add(new Exception("Identity(elli_idt) in Jwt should be either Application or Enterprise"));
                                    }
                                  
                                }
                            }

                            foreach (var jwtHandlerException in jwtHandlerExceptions)
                            {
                                Logger.Log(LogLevel.Error, "epps", "Unexpected exception during jwt processing.", jwtHandlerException);
                                Logger.Log(LogLevel.Error, "epps", $"InvalidToken={token}");
                            }

                            context.ErrorResult = new ResponseMessageResult(context.Request.CreateResponse<Error>(
                                HttpStatusCode.Unauthorized,
                                new Error()
                                {
                                    Code = ErrorConstants.UnauthorizedInvalidCode,
                                    Summary = ErrorConstants.UnauthorizedInvalidSummary
                                }));
                            return;
                        }
                    }
                }

                if (context.ActionContext.AllowAnonymous())
                {
                    context.Principal = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            new Claim[]
                            {
                                new Claim(ClaimTypes.Name, "urn:elli:service:epps:anonymous")
                            },
                            AuthenticationTypes.Federation));
                    return;
                }

                context.ErrorResult = new ResponseMessageResult(context.Request.CreateResponse<Error>(
                    HttpStatusCode.Unauthorized,
                    new Error()
                    {
                        Code = ErrorConstants.UnauthorizedMissingCode,
                        Summary = ErrorConstants.UnauthorizedMissingSummary
                    }));
                return;
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "epps", "Exception while attempting to authenticate.Authorization header is :" + context.Request.Headers.Authorization, ex);

                context.ErrorResult = new ResponseMessageResult(context.Request.CreateResponse<Error>(
                    HttpStatusCode.InternalServerError,
                    new Error()
                    {
                        Code = ErrorConstants.InternalServerErrorCode,
                        Summary = ErrorConstants.InternalServerErrorSummary,
                        Details = ErrorConstants.InternalServerErrorDetails
                    }));
            }
        }

              
        private bool IsApplicationIdentity(ClaimsIdentity identity)
        {
            string identityType = identity.Claims.FirstOrDefault(claim => claim.Type.Equals(ElliJwtConstants.ClaimTypeIdentityType, StringComparison.OrdinalIgnoreCase))?.Value;
            return !string.IsNullOrWhiteSpace(identityType) && identityType.Equals(ElliJwtConstants.ClaimValueIdentityTypeApplication, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsEnterpriseIdentity(ClaimsIdentity identity)
        {
            string identityType = identity.Claims.FirstOrDefault(claim => claim.Type.Equals(ElliJwtConstants.ClaimTypeIdentityType, StringComparison.OrdinalIgnoreCase))?.Value;
            return !string.IsNullOrWhiteSpace(identityType) && identityType.Equals(ElliJwtConstants.ClaimValueIdentityTypeEnterprise, StringComparison.OrdinalIgnoreCase);
        }

        public async Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken)
        {
        }

        private IEnumerable<Claim> FlattenClaims(IDictionary<string, object> claims, string roleType)
        {
            var flattenedClaims = new List<Claim>();
            foreach (var claim in claims)
            {
                this.FlattenClaims(flattenedClaims, claim.Key, claim.Value, roleType);
            }

            return flattenedClaims;
        }

        private void FlattenClaims(IList<Claim> claims, string claimType, object claimValue, string roleType)
        {
            if (Type.GetTypeCode(claimValue.GetType()) == TypeCode.Object)
            {
                var claimObjectMap = claimValue as IEnumerable<KeyValuePair<string, object>>;
                if (claimObjectMap != null)
                {
                    foreach (var entry in claimObjectMap)
                    {
                        this.FlattenClaims(claims, $"{claimType}_{entry.Key}", entry.Value, roleType);
                    }

                    return;
                }

                var claimStringMap = claimValue as IEnumerable<KeyValuePair<string, string>>;
                if (claimStringMap != null)
                {
                    foreach (var entry in claimStringMap)
                    {
                        this.FlattenClaims(claims, $"{claimType}_{entry.Key}", entry.Value, roleType);
                    }

                    return;
                }

                var claimArray = claimValue as IEnumerable;
                if (claimArray != null)
                {
                    foreach (var value in claimArray)
                    {
                        this.FlattenClaims(claims, claimType, value, roleType);
                    }

                    return;
                }
            }

            if (claimType.Equals(roleType))
            {
                claims.Add(new Claim(claimType, claimValue.ToString().ToLowerInvariant()));
                // TODO: validate if necessary - if not remove
                claims.Add(new Claim(ClaimTypes.Role, claimValue.ToString().ToLowerInvariant()));
            }
            else
            {
                claims.Add(new Claim(claimType, claimValue.ToString()));
            }
        }
    }
}