package com.sama.shop.data

import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import okhttp3.Authenticator
import okhttp3.Interceptor
import okhttp3.Request
import okhttp3.Response
import okhttp3.Route
import javax.inject.Inject
import javax.inject.Provider
import javax.inject.Singleton

/** Paths that must never carry a bearer token, and must never trigger a renewal. */
private val ANONYMOUS_PATHS = listOf(
    "/api/v1/auth/login",
    "/api/v1/auth/register",
    "/api/v1/auth/refresh",
)

private fun Request.isAnonymous(): Boolean =
    ANONYMOUS_PATHS.any { url.encodedPath.startsWith(it) }

/** Attaches the current access token to every authenticated request. */
@Singleton
class AuthHeaderInterceptor @Inject constructor(
    private val tokens: TokenStore,
) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()

        if (request.isAnonymous() || request.header("Authorization") != null) {
            return chain.proceed(request)
        }

        val token = tokens.accessToken() ?: return chain.proceed(request)

        return chain.proceed(
            request.newBuilder().header("Authorization", "Bearer $token").build(),
        )
    }
}

/**
 * Renews an expired access token and replays the request.
 *
 * OkHttp calls an [Authenticator] only after a 401, and only for requests it can retry, which
 * makes this the right place for renewal rather than an interceptor that would have to inspect
 * every response itself.
 *
 * Two details are load-bearing:
 *
 *  - the mutex means concurrent 401s produce **one** refresh. Sama rotates refresh tokens and
 *    treats reuse as theft, so two parallel refreshes would revoke the whole token family and
 *    sign the user out — the exact opposite of what renewal is for.
 *  - after taking the lock, the token is re-read. If another thread already renewed while this
 *    one waited, the request is simply retried with the new token instead of refreshing again.
 */
@Singleton
class TokenRefreshAuthenticator @Inject constructor(
    private val tokens: TokenStore,
    // Provider breaks the dependency cycle: the API is built on the OkHttp client that this
    // authenticator is part of.
    private val api: Provider<SamaApi>,
    private val sessionEvents: SessionEvents,
) : Authenticator {

    private val refreshMutex = Mutex()

    override fun authenticate(route: Route?, response: Response): Request? {
        if (response.request.isAnonymous()) {
            return null
        }

        // More than one prior attempt means renewal is not the problem; giving up avoids an
        // infinite retry loop against an endpoint the user genuinely cannot reach.
        if (response.priorResponseCount() >= 1) {
            return null
        }

        val staleToken = response.request.header("Authorization")?.removePrefix("Bearer ")

        val renewed = runBlocking {
            refreshMutex.withLock {
                val current = tokens.accessToken()

                // Someone else refreshed while this thread waited for the lock.
                if (current != null && current != staleToken) {
                    return@withLock current
                }

                val refreshToken = tokens.refreshToken() ?: return@withLock null

                val result = runCatching { api.get().refresh(RefreshRequest(refreshToken)) }
                    .getOrNull()

                val body = result?.body()

                if (result?.isSuccessful == true && body != null) {
                    tokens.save(body)
                    body.accessToken
                } else {
                    // The refresh token is dead — expired, revoked, or replayed. Clearing it
                    // and signalling the UI is the only way out; retrying cannot help.
                    tokens.clear()
                    sessionEvents.notifySessionExpired()
                    null
                }
            }
        } ?: return null

        return response.request.newBuilder()
            .header("Authorization", "Bearer $renewed")
            .build()
    }

    private fun Response.priorResponseCount(): Int {
        var count = 0
        var prior = priorResponse
        while (prior != null) {
            count++
            prior = prior.priorResponse
        }
        return count
    }
}
