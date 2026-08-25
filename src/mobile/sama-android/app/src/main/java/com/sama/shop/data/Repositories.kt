package com.sama.shop.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import retrofit2.Response
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

/** A call that either produced a value or a reason it did not. */
sealed interface ApiResult<out T> {
    data class Success<T>(val value: T) : ApiResult<T>
    data class Failure(
        val status: Int,
        val message: String,
        val code: String? = null,
        val fieldErrors: Map<String, List<String>>? = null,
    ) : ApiResult<Nothing>
}

/**
 * Turns a Retrofit [Response] into an [ApiResult], decoding the API's problem document so the
 * UI can show what actually went wrong rather than a status code.
 */
class ApiResponseMapper @Inject constructor(private val json: Json) {

    fun <T> map(response: Response<T>): ApiResult<T> {
        val body = response.body()

        if (response.isSuccessful && body != null) {
            return ApiResult.Success(body)
        }

        // A 204 is successful and has no body; Unit-returning calls land here legitimately.
        if (response.isSuccessful) {
            @Suppress("UNCHECKED_CAST")
            return ApiResult.Success(Unit as T)
        }

        val problem = runCatching {
            response.errorBody()?.string()?.takeIf { it.isNotBlank() }
                ?.let { json.decodeFromString<ProblemDetails>(it) }
        }.getOrNull()

        return ApiResult.Failure(
            status = response.code(),
            message = problem?.detail
                ?: problem?.title
                ?: defaultMessage(response.code()),
            code = problem?.code,
            fieldErrors = problem?.fieldErrors,
        )
    }

    private fun defaultMessage(status: Int): String = when (status) {
        401 -> "Please sign in again."
        403 -> "Your account does not have permission for that."
        404 -> "That could not be found."
        409 -> "That conflicts with the current state. Reload and try again."
        429 -> "Too many requests. Please slow down."
        in 500..599 -> "Something went wrong at our end. Please try again."
        else -> "The request could not be completed."
    }
}

@Singleton
class AuthRepository @Inject constructor(
    private val api: SamaApi,
    private val tokens: TokenStore,
    private val mapper: ApiResponseMapper,
) {
    private val _profile = MutableStateFlow<UserProfile?>(null)
    val profile: StateFlow<UserProfile?> = _profile.asStateFlow()

    fun isSignedIn(): Boolean = tokens.hasSession()

    fun hasPermission(permission: String): Boolean =
        _profile.value?.permissions?.contains(permission) == true

    suspend fun signIn(email: String, password: String): ApiResult<UserProfile> =
        establish { api.login(LoginRequest(email.trim(), password)) }

    suspend fun register(email: String, password: String, displayName: String): ApiResult<UserProfile> =
        establish { api.register(RegisterRequest(email.trim(), password, displayName.trim())) }

    /**
     * Rebuilds the session on launch from the stored refresh token, so a returning user is not
     * asked to sign in every time the app is cold-started.
     */
    suspend fun restore(): Boolean {
        val refreshToken = tokens.refreshToken() ?: return false

        return when (val result = mapper.map(api.refresh(RefreshRequest(refreshToken)))) {
            is ApiResult.Success -> {
                tokens.save(result.value)
                loadProfile()
                true
            }
            is ApiResult.Failure -> {
                tokens.clear()
                false
            }
        }
    }

    suspend fun signOut() {
        // Best effort: the local session must be cleared whether or not the server is reachable.
        runCatching { api.logout(LogoutRequest(tokens.refreshToken())) }
        tokens.clear()
        _profile.value = null
    }

    private suspend fun establish(call: suspend () -> Response<TokenResponse>): ApiResult<UserProfile> =
        when (val result = mapper.map(call())) {
            is ApiResult.Success -> {
                tokens.save(result.value)
                loadProfile()
                    ?.let { ApiResult.Success(it) }
                    ?: ApiResult.Failure(500, "Signed in, but your profile could not be loaded.")
            }
            is ApiResult.Failure -> result
        }

    private suspend fun loadProfile(): UserProfile? =
        (mapper.map(api.profile()) as? ApiResult.Success)?.value?.also { _profile.value = it }
}

@Singleton
class CatalogRepository @Inject constructor(
    private val api: SamaApi,
    private val mapper: ApiResponseMapper,
) {
    suspend fun products(
        page: Int = 1,
        pageSize: Int = 20,
        query: String? = null,
        categoryId: String? = null,
        sort: String? = null,
    ): ApiResult<Paged<ProductSummary>> =
        mapper.map(api.products(page, pageSize, query?.ifBlank { null }, categoryId, sort))

    suspend fun product(slug: String): ApiResult<ProductDetail> = mapper.map(api.product(slug))

    suspend fun categories(): ApiResult<List<CategoryNode>> = mapper.map(api.categories())
}

@Singleton
class BasketRepository @Inject constructor(
    private val api: SamaApi,
    private val mapper: ApiResponseMapper,
) {
    private val _basket = MutableStateFlow<Basket?>(null)
    val basket: StateFlow<Basket?> = _basket.asStateFlow()

    suspend fun refresh(): ApiResult<Basket> = remember(mapper.map(api.basket()))

    suspend fun add(productId: String, quantity: Int = 1): ApiResult<Basket> =
        remember(mapper.map(api.addToBasket(AddItemRequest(productId, quantity))))

    suspend fun setQuantity(productId: String, quantity: Int): ApiResult<Basket> =
        remember(mapper.map(api.setQuantity(productId, SetQuantityRequest(quantity))))

    suspend fun remove(productId: String): ApiResult<Basket> =
        remember(mapper.map(api.removeFromBasket(productId)))

    suspend fun clear() {
        mapper.map(api.clearBasket())
        _basket.value = null
    }

    /** Dropped after checkout: the server empties the basket when the order is confirmed. */
    fun invalidate() {
        _basket.value = null
    }

    // Every mutation returns the server's view, and that is what is stored. The server clamps
    // quantities and re-prices lines, so a locally-patched basket would drift from what
    // checkout actually charges.
    private fun remember(result: ApiResult<Basket>): ApiResult<Basket> =
        result.also { if (it is ApiResult.Success) _basket.value = it.value }
}

@Singleton
class OrderRepository @Inject constructor(
    private val api: SamaApi,
    private val mapper: ApiResponseMapper,
) {
    /**
     * Places an order.
     *
     * The caller supplies [idempotencyKey] and keeps it across retries of the same attempt.
     * On a mobile connection a dropped response is routine, and without this a retry would
     * place a second order and take a second payment.
     */
    suspend fun checkout(
        address: ShippingAddress,
        idempotencyKey: String,
    ): ApiResult<CheckoutAccepted> =
        mapper.map(api.checkout(idempotencyKey, CheckoutRequest(address)))

    suspend fun orders(page: Int = 1): ApiResult<Paged<OrderSummary>> = mapper.map(api.orders(page))

    suspend fun order(id: String): ApiResult<OrderDetail> = mapper.map(api.order(id))

    suspend fun cancel(id: String, reason: String?): ApiResult<OrderDetail> =
        mapper.map(api.cancelOrder(id, CancelOrderRequest(reason)))

    companion object {
        fun newIdempotencyKey(): String = UUID.randomUUID().toString()
    }
}
