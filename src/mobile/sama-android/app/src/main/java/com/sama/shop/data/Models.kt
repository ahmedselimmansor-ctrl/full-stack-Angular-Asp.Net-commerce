package com.sama.shop.data

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * Wire contracts shared with the Sama API.
 *
 * Every response class is annotated `@Serializable` and deserialised by a Json configured with
 * `ignoreUnknownKeys = true`. That tolerance is deliberate: an installed app cannot be updated
 * in step with the backend, so a field added server-side must not break clients that are
 * months behind.
 */

@Serializable
data class Paged<T>(
    val items: List<T> = emptyList(),
    val page: Int = 1,
    val pageSize: Int = 20,
    val totalCount: Long = 0,
    val totalPages: Int = 0,
    val hasPrevious: Boolean = false,
    val hasNext: Boolean = false,
)

@Serializable
data class ProductSummary(
    val id: String,
    val sku: String,
    val name: String,
    val slug: String,
    val brand: String? = null,
    val price: Double,
    val currency: String,
    val categoryId: String,
    val primaryImageUrl: String? = null,
)

@Serializable
data class ProductDetail(
    val id: String,
    val sku: String,
    val name: String,
    val slug: String,
    val description: String,
    val brand: String? = null,
    val price: Double,
    val currency: String,
    val status: String,
    val categoryId: String,
    val images: List<ProductImage> = emptyList(),
)

@Serializable
data class ProductImage(val url: String, val altText: String? = null, val sortOrder: Int = 0)

@Serializable
data class CategoryNode(
    val id: String,
    val name: String,
    val slug: String,
    val sortOrder: Int = 0,
    val children: List<CategoryNode> = emptyList(),
)

@Serializable
data class Basket(
    val ownerId: String,
    val items: List<BasketLine> = emptyList(),
    val totalQuantity: Int = 0,
    val subtotal: Double = 0.0,
    val currency: String = "USD",
    val updatedAt: String,
)

@Serializable
data class BasketLine(
    val productId: String,
    val sku: String,
    val name: String,
    val quantity: Int,
    val unitPrice: Double,
    val lineTotal: Double,
    val currency: String,
    val imageUrl: String? = null,
)

@Serializable
data class AddItemRequest(val productId: String, val quantity: Int)

@Serializable
data class SetQuantityRequest(val quantity: Int)

@Serializable
data class OrderSummary(
    val id: String,
    val orderNumber: String,
    val status: String,
    val total: Double,
    val currency: String,
    val lineCount: Int,
    val createdAt: String,
)

@Serializable
data class OrderDetail(
    val id: String,
    val orderNumber: String,
    val status: String,
    val total: Double,
    val currency: String,
    val lines: List<OrderLine> = emptyList(),
    val shippingAddress: ShippingAddress,
    val cancellationReason: String? = null,
    val createdAt: String,
    val confirmedAt: String? = null,
)

@Serializable
data class OrderLine(
    val productId: String,
    val sku: String,
    val name: String,
    val quantity: Int,
    val unitPrice: Double,
    val lineTotal: Double,
)

@Serializable
data class ShippingAddress(
    val line1: String,
    val city: String,
    val postcode: String,
    val country: String,
)

@Serializable
data class CheckoutRequest(val shippingAddress: ShippingAddress)

@Serializable
data class CheckoutAccepted(
    val id: String,
    val orderNumber: String,
    val status: String,
    val total: Double,
    val currency: String,
)

@Serializable
data class CancelOrderRequest(val reason: String? = null)

// --- Authentication ---------------------------------------------------------------------

@Serializable
data class LoginRequest(val email: String, val password: String)

@Serializable
data class RegisterRequest(val email: String, val password: String, val displayName: String)

@Serializable
data class RefreshRequest(val refreshToken: String)

@Serializable
data class LogoutRequest(val refreshToken: String? = null)

@Serializable
data class TokenResponse(
    val accessToken: String,
    val refreshToken: String,
    val tokenType: String = "Bearer",
    val expiresIn: Int = 900,
)

@Serializable
data class UserProfile(
    val id: String,
    val email: String,
    val displayName: String,
    val emailConfirmed: Boolean = false,
    val roles: List<String> = emptyList(),
    val permissions: List<String> = emptyList(),
)

/** RFC 9457 problem document, returned by every Sama failure path. */
@Serializable
data class ProblemDetails(
    val type: String? = null,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
    val code: String? = null,
    val traceId: String? = null,
    @SerialName("errors") val fieldErrors: Map<String, List<String>>? = null,
)
