package com.sama.shop

import com.sama.shop.data.OrderSummary
import com.sama.shop.data.Paged
import com.sama.shop.data.ProblemDetails
import com.sama.shop.data.ProductSummary
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Wire-contract tolerance.
 *
 * An installed app cannot be updated in step with the backend, so the decoder's behaviour when
 * the server is ahead of the client is a real compatibility property, not a detail.
 */
class ModelSerializationTest {

    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
        coerceInputValues = true
    }

    @Test
    fun `ignores fields the installed build has never heard of`() {
        // The server adding a field must not break clients that are months behind.
        val payload = """
            {
              "id": "01a0-1", "sku": "SKU-1", "name": "Espresso", "slug": "espresso",
              "price": 19.99, "currency": "USD", "categoryId": "cat-1",
              "loyaltyPoints": 120, "badge": { "kind": "new" }
            }
        """.trimIndent()

        val product = json.decodeFromString<ProductSummary>(payload)

        assertEquals("Espresso", product.name)
        assertEquals(19.99, product.price, 0.0)
    }

    @Test
    fun `treats an absent optional field as null rather than failing`() {
        val product = json.decodeFromString<ProductSummary>(
            """{"id":"1","sku":"S","name":"N","slug":"n","price":1.0,"currency":"USD","categoryId":"c"}""",
        )

        assertNull(product.brand)
        assertNull(product.primaryImageUrl)
    }

    @Test
    fun `decodes an empty page without items present`() {
        // A paged endpoint with nothing to return omits the array entirely in some responses.
        val page = json.decodeFromString<Paged<OrderSummary>>("""{"page":1,"pageSize":20,"totalCount":0}""")

        assertTrue(page.items.isEmpty())
        assertEquals(0L, page.totalCount)
    }

    @Test
    fun `decodes the problem document the API returns on failure`() {
        val problem = json.decodeFromString<ProblemDetails>(
            """
            {
              "type": "about:blank", "title": "Conflict", "status": 409,
              "detail": "Only 2 of SKU-1 are available.",
              "code": "inventory.stock.insufficient"
            }
            """.trimIndent(),
        )

        // The code is what the app branches on; the detail is what it shows a person.
        assertEquals("inventory.stock.insufficient", problem.code)
        assertEquals(409, problem.status)
        assertTrue(problem.detail!!.contains("available"))
    }

    @Test
    fun `decodes per-field validation errors`() {
        val problem = json.decodeFromString<ProblemDetails>(
            """{"status":400,"errors":{"Password":["Use at least 12 characters."]}}""",
        )

        assertEquals(listOf("Use at least 12 characters."), problem.fieldErrors?.get("Password"))
    }
}
