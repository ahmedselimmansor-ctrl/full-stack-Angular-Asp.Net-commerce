package com.sama.shop.data

import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.PATCH
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * The Sama API as the app sees it: one origin, the gateway.
 *
 * Every call returns `Response<T>` rather than `T`, so a non-2xx status is an ordinary value
 * the repository inspects instead of an exception thrown from deep inside Retrofit.
 */
interface SamaApi {

    // --- Catalogue (public) ---------------------------------------------------------------

    @GET("api/v1/products")
    suspend fun products(
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 20,
        @Query("q") query: String? = null,
        @Query("categoryId") categoryId: String? = null,
        @Query("sort") sort: String? = null,
    ): Response<Paged<ProductSummary>>

    @GET("api/v1/products/{slug}")
    suspend fun product(@Path("slug") slug: String): Response<ProductDetail>

    @GET("api/v1/categories")
    suspend fun categories(): Response<List<CategoryNode>>

    // --- Authentication ---------------------------------------------------------------------

    @POST("api/v1/auth/login")
    suspend fun login(@Body request: LoginRequest): Response<TokenResponse>

    @POST("api/v1/auth/register")
    suspend fun register(@Body request: RegisterRequest): Response<TokenResponse>

    @POST("api/v1/auth/refresh")
    suspend fun refresh(@Body request: RefreshRequest): Response<TokenResponse>

    @POST("api/v1/auth/logout")
    suspend fun logout(@Body request: LogoutRequest): Response<Unit>

    @GET("api/v1/auth/me")
    suspend fun profile(): Response<UserProfile>

    // --- Basket ------------------------------------------------------------------------------

    @GET("api/v1/basket")
    suspend fun basket(): Response<Basket>

    @POST("api/v1/basket/items")
    suspend fun addToBasket(@Body request: AddItemRequest): Response<Basket>

    @PUT("api/v1/basket/items/{productId}")
    suspend fun setQuantity(
        @Path("productId") productId: String,
        @Body request: SetQuantityRequest,
    ): Response<Basket>

    @DELETE("api/v1/basket/items/{productId}")
    suspend fun removeFromBasket(@Path("productId") productId: String): Response<Basket>

    @DELETE("api/v1/basket")
    suspend fun clearBasket(): Response<Unit>

    // --- Orders --------------------------------------------------------------------------------

    /**
     * Places an order.
     *
     * The idempotency key is supplied by the caller and reused across retries of the same
     * attempt, which is what makes a dropped response safe on a mobile connection — the exact
     * situation this header exists for.
     */
    @POST("api/v1/orders")
    suspend fun checkout(
        @Header("Idempotency-Key") idempotencyKey: String,
        @Body request: CheckoutRequest,
    ): Response<CheckoutAccepted>

    @GET("api/v1/orders")
    suspend fun orders(
        @Query("page") page: Int = 1,
        @Query("pageSize") pageSize: Int = 20,
    ): Response<Paged<OrderSummary>>

    @GET("api/v1/orders/{id}")
    suspend fun order(@Path("id") id: String): Response<OrderDetail>

    @POST("api/v1/orders/{id}/cancel")
    suspend fun cancelOrder(
        @Path("id") id: String,
        @Body request: CancelOrderRequest,
    ): Response<OrderDetail>
}
