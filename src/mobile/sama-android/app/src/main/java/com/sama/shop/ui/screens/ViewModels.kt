package com.sama.shop.ui.screens

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.sama.shop.data.ApiResult
import com.sama.shop.data.AuthRepository
import com.sama.shop.data.Basket
import com.sama.shop.data.BasketRepository
import com.sama.shop.data.CatalogRepository
import com.sama.shop.data.OrderDetail
import com.sama.shop.data.OrderRepository
import com.sama.shop.data.OrderSummary
import com.sama.shop.data.ProductDetail
import com.sama.shop.data.ProductSummary
import com.sama.shop.data.ShippingAddress
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

// --- Catalogue ---------------------------------------------------------------------------

data class CatalogState(
    val products: List<ProductSummary> = emptyList(),
    val query: String = "",
    val loading: Boolean = true,
    val error: String? = null,
    val page: Int = 1,
    val hasNext: Boolean = false,
)

@HiltViewModel
class CatalogViewModel @Inject constructor(
    private val catalog: CatalogRepository,
    private val basket: BasketRepository,
    private val auth: AuthRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(CatalogState())
    val state: StateFlow<CatalogState> = _state.asStateFlow()

    /** Cancelled and restarted as the shopper types, so only the final term reaches the API. */
    private var searchJob: Job? = null

    init {
        load()
    }

    fun onQueryChanged(query: String) {
        _state.value = _state.value.copy(query = query)

        searchJob?.cancel()
        searchJob = viewModelScope.launch {
            // The search endpoint hits the database and is rate-limited far more tightly than
            // the plain listing; debouncing keeps a fast typist from exhausting that bucket.
            delay(350)
            load(page = 1)
        }
    }

    fun load(page: Int = _state.value.page) {
        viewModelScope.launch {
            _state.value = _state.value.copy(loading = true, error = null)

            when (val result = catalog.products(page = page, query = _state.value.query)) {
                is ApiResult.Success -> _state.value = _state.value.copy(
                    products = result.value.items,
                    page = result.value.page,
                    hasNext = result.value.hasNext,
                    loading = false,
                )
                is ApiResult.Failure -> _state.value = _state.value.copy(
                    loading = false,
                    error = result.message,
                )
            }
        }
    }

    fun addToBasket(product: ProductSummary, onResult: (String) -> Unit) {
        if (!auth.isSignedIn()) {
            onResult("Sign in to start a basket.")
            return
        }

        viewModelScope.launch {
            when (val result = basket.add(product.id)) {
                is ApiResult.Success -> onResult("${product.name} added to your basket.")
                is ApiResult.Failure -> onResult(result.message)
            }
        }
    }
}

// --- Product detail -----------------------------------------------------------------------

@HiltViewModel
class ProductViewModel @Inject constructor(
    private val catalog: CatalogRepository,
    private val basket: BasketRepository,
) : ViewModel() {

    private val _product = MutableStateFlow<ProductDetail?>(null)
    val product: StateFlow<ProductDetail?> = _product.asStateFlow()

    private val _loading = MutableStateFlow(true)
    val loading: StateFlow<Boolean> = _loading.asStateFlow()

    fun load(slug: String) {
        viewModelScope.launch {
            _loading.value = true
            _product.value = (catalog.product(slug) as? ApiResult.Success)?.value
            _loading.value = false
        }
    }

    fun addToBasket(quantity: Int, onResult: (String) -> Unit) {
        val current = _product.value ?: return

        viewModelScope.launch {
            when (val result = basket.add(current.id, quantity)) {
                is ApiResult.Success -> onResult("${current.name} added to your basket.")
                is ApiResult.Failure -> onResult(result.message)
            }
        }
    }
}

// --- Basket ---------------------------------------------------------------------------------

@HiltViewModel
class BasketViewModel @Inject constructor(
    private val repository: BasketRepository,
) : ViewModel() {

    val basket: StateFlow<Basket?> = repository.basket

    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()

    init {
        refresh()
    }

    fun refresh() = run { repository.refresh() }

    fun setQuantity(productId: String, quantity: Int) = run {
        repository.setQuantity(productId, quantity)
    }

    fun remove(productId: String) = run { repository.remove(productId) }

    private fun run(operation: suspend () -> Unit) {
        viewModelScope.launch {
            _busy.value = true
            try {
                operation()
            } finally {
                // Always released: a failed call must not leave the UI permanently disabled.
                _busy.value = false
            }
        }
    }
}

// --- Checkout -----------------------------------------------------------------------------

data class CheckoutState(
    val submitting: Boolean = false,
    val error: String? = null,
    val placedOrderId: String? = null,
)

@HiltViewModel
class CheckoutViewModel @Inject constructor(
    private val orders: OrderRepository,
    private val basket: BasketRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(CheckoutState())
    val state: StateFlow<CheckoutState> = _state.asStateFlow()

    /**
     * One key per checkout attempt, reused across retries of that attempt.
     *
     * This is what makes a dropped response safe on a mobile connection: retrying replays the
     * original outcome instead of placing a second order.
     */
    private var idempotencyKey: String? = null

    fun placeOrder(address: ShippingAddress) {
        viewModelScope.launch {
            _state.value = _state.value.copy(submitting = true, error = null)

            val key = idempotencyKey ?: OrderRepository.newIdempotencyKey().also { idempotencyKey = it }

            when (val result = orders.checkout(address, key)) {
                is ApiResult.Success -> {
                    // Only cleared once an order actually exists; a failed attempt keeps its
                    // key so a retry is still recognised as the same request.
                    idempotencyKey = null
                    basket.invalidate()
                    _state.value = CheckoutState(placedOrderId = result.value.id)
                }
                is ApiResult.Failure -> _state.value =
                    _state.value.copy(submitting = false, error = result.message)
            }
        }
    }
}

// --- Orders --------------------------------------------------------------------------------

@HiltViewModel
class OrdersViewModel @Inject constructor(
    private val orders: OrderRepository,
) : ViewModel() {

    private val _list = MutableStateFlow<List<OrderSummary>>(emptyList())
    val list: StateFlow<List<OrderSummary>> = _list.asStateFlow()

    private val _loading = MutableStateFlow(true)
    val loading: StateFlow<Boolean> = _loading.asStateFlow()

    private val _detail = MutableStateFlow<OrderDetail?>(null)
    val detail: StateFlow<OrderDetail?> = _detail.asStateFlow()

    private var pollJob: Job? = null

    init {
        refresh()
    }

    fun refresh() {
        viewModelScope.launch {
            _loading.value = true
            _list.value = (orders.orders() as? ApiResult.Success)?.value?.items ?: emptyList()
            _loading.value = false
        }
    }

    /**
     * Loads an order and, while the saga is still unsettled, keeps refreshing it.
     *
     * Checkout returns 202 with the order still resolving stock and payment, so the first view
     * of a new order is almost always mid-flight. The poll stops as soon as it settles — a
     * fixed-interval poll that never terminates is a slow drain on battery and backend alike.
     */
    fun loadDetail(id: String) {
        pollJob?.cancel()

        pollJob = viewModelScope.launch {
            var attempts = 0

            while (attempts < MAX_POLLS) {
                val order = (orders.order(id) as? ApiResult.Success)?.value
                _detail.value = order

                if (order == null || order.status !in UNSETTLED) {
                    return@launch
                }

                attempts++
                delay(POLL_INTERVAL_MS)
            }
        }
    }

    fun cancel(id: String, onResult: (String) -> Unit) {
        viewModelScope.launch {
            when (val result = orders.cancel(id, "Cancelled by the customer.")) {
                is ApiResult.Success -> {
                    _detail.value = result.value
                    onResult("Your order has been cancelled.")
                }
                is ApiResult.Failure -> onResult(result.message)
            }
        }
    }

    override fun onCleared() {
        pollJob?.cancel()
        super.onCleared()
    }

    private companion object {
        val UNSETTLED = setOf("Pending", "AwaitingPayment")
        const val POLL_INTERVAL_MS = 2_000L
        const val MAX_POLLS = 45
    }
}

// --- Authentication ---------------------------------------------------------------------

data class AuthState(
    val busy: Boolean = false,
    val error: String? = null,
    val fieldErrors: Map<String, List<String>> = emptyMap(),
    val signedIn: Boolean = false,
)

@HiltViewModel
class AuthViewModel @Inject constructor(
    private val auth: AuthRepository,
) : ViewModel() {

    private val _state = MutableStateFlow(AuthState(signedIn = auth.isSignedIn()))
    val state: StateFlow<AuthState> = _state.asStateFlow()

    val profile = auth.profile

    fun signIn(email: String, password: String) = submit { auth.signIn(email, password) }

    fun register(email: String, password: String, displayName: String) =
        submit { auth.register(email, password, displayName) }

    fun signOut() {
        viewModelScope.launch {
            auth.signOut()
            _state.value = AuthState(signedIn = false)
        }
    }

    private fun submit(operation: suspend () -> ApiResult<*>) {
        viewModelScope.launch {
            _state.value = _state.value.copy(busy = true, error = null, fieldErrors = emptyMap())

            when (val result = operation()) {
                is ApiResult.Success -> _state.value = AuthState(signedIn = true)
                is ApiResult.Failure -> _state.value = _state.value.copy(
                    busy = false,
                    // 401 on sign-in is a wrong password, not a session problem; the API
                    // deliberately does not say which of the two fields was wrong, because
                    // saying so would turn the form into an account-enumeration oracle.
                    error = if (result.status == 401) {
                        "That email address or password is incorrect."
                    } else {
                        result.message
                    },
                    fieldErrors = result.fieldErrors ?: emptyMap(),
                )
            }
        }
    }
}
