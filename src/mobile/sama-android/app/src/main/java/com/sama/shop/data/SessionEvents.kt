package com.sama.shop.data

import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Carries "the session ended" from the network layer, where it is discovered, to the UI, which
 * has to react by navigating to sign-in.
 *
 * A SharedFlow rather than a state holder: this is an event that happens once, and replaying
 * it to a newly-subscribed screen would bounce the user out of a sign-in they just started.
 */
@Singleton
class SessionEvents @Inject constructor() {

    private val _sessionExpired = MutableSharedFlow<Unit>(
        replay = 0,
        extraBufferCapacity = 1,
        // The authenticator runs on an OkHttp thread and must never block; dropping a
        // duplicate signal costs nothing because one is enough.
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )

    val sessionExpired: SharedFlow<Unit> = _sessionExpired

    fun notifySessionExpired() {
        _sessionExpired.tryEmit(Unit)
    }
}
