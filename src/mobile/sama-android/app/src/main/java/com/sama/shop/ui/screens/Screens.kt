package com.sama.shop.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ShoppingCart
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.clickable
import androidx.compose.foundation.text.KeyboardOptions
import coil.compose.AsyncImage
import com.sama.shop.data.OrderSummary
import com.sama.shop.data.ProductSummary
import com.sama.shop.data.ShippingAddress
import com.sama.shop.ui.formatMoney
import com.sama.shop.ui.orderStatusLabel
import kotlinx.coroutines.launch

// --- Catalogue ---------------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CatalogScreen(
    viewModel: CatalogViewModel,
    snackbarHostState: SnackbarHostState,
    onProductSelected: (ProductSummary) -> Unit,
    onOpenBasket: () -> Unit,
    onOpenOrders: () -> Unit,
    onSignIn: () -> Unit,
) {
    val state by viewModel.state.collectAsState()
    val scope = rememberCoroutineScope()

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text("Sama") },
                actions = {
                    IconButton(onClick = onOpenBasket) {
                        Icon(Icons.Filled.ShoppingCart, contentDescription = "Basket")
                    }
                    TextButton(onClick = onOpenOrders) { Text("Orders") }
                    TextButton(onClick = onSignIn) { Text("Account") }
                },
            )
        },
    ) { padding ->
        Column(Modifier.padding(padding).fillMaxSize()) {
            OutlinedTextField(
                value = state.query,
                onValueChange = viewModel::onQueryChanged,
                label = { Text("Search") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(16.dp),
            )

            when {
                state.loading && state.products.isEmpty() ->
                    Box(Modifier.fillMaxSize(), Alignment.Center) { CircularProgressIndicator() }

                state.error != null ->
                    Box(Modifier.fillMaxSize(), Alignment.Center) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Text("We couldn't load the catalogue", style = MaterialTheme.typography.titleMedium)
                            Spacer(Modifier.height(8.dp))
                            Button(onClick = { viewModel.load() }) { Text("Try again") }
                        }
                    }

                state.products.isEmpty() ->
                    Box(Modifier.fillMaxSize(), Alignment.Center) { Text("Nothing matched that search.") }

                else -> LazyColumn(
                    contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    // Keyed on the product id: without a stable key, Compose reuses rows by
                    // position and images visibly swap between items as the list changes.
                    items(state.products, key = { it.id }) { product ->
                        ProductRow(
                            product = product,
                            onClick = { onProductSelected(product) },
                            onAdd = {
                                viewModel.addToBasket(product) { message ->
                                    scope.launch { snackbarHostState.showSnackbar(message) }
                                }
                            },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ProductRow(product: ProductSummary, onClick: () -> Unit, onAdd: () -> Unit) {
    Card(Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            AsyncImage(
                model = product.primaryImageUrl,
                contentDescription = product.name,
                contentScale = ContentScale.Crop,
                modifier = Modifier.size(72.dp).clip(RoundedCornerShape(8.dp)),
            )

            Spacer(Modifier.size(12.dp))

            Column(Modifier.weight(1f)) {
                product.brand?.let {
                    Text(it.uppercase(), style = MaterialTheme.typography.labelSmall)
                }
                Text(product.name, style = MaterialTheme.typography.titleMedium)
                Text(
                    formatMoney(product.price, product.currency),
                    style = MaterialTheme.typography.bodyLarge,
                )
            }

            Button(onClick = onAdd) { Text("Add") }
        }
    }
}

// --- Product detail -----------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ProductScreen(
    slug: String,
    viewModel: ProductViewModel,
    snackbarHostState: SnackbarHostState,
    onBack: () -> Unit,
) {
    val product by viewModel.product.collectAsState()
    val loading by viewModel.loading.collectAsState()
    val scope = rememberCoroutineScope()
    var quantity by remember { mutableStateOf(1) }

    LaunchedEffect(slug) { viewModel.load(slug) }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text(product?.name ?: "Product") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        when {
            loading -> Box(Modifier.padding(padding).fillMaxSize(), Alignment.Center) {
                CircularProgressIndicator()
            }

            product == null -> Box(Modifier.padding(padding).fillMaxSize(), Alignment.Center) {
                Text("This product is no longer available.")
            }

            else -> {
                val item = product!!

                Column(
                    Modifier.padding(padding).fillMaxSize().padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    AsyncImage(
                        model = item.images.firstOrNull()?.url,
                        contentDescription = item.name,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxWidth().height(220.dp).clip(RoundedCornerShape(12.dp)),
                    )

                    Text(item.name, style = MaterialTheme.typography.headlineSmall)
                    Text(formatMoney(item.price, item.currency), style = MaterialTheme.typography.titleLarge)
                    Text(item.description, style = MaterialTheme.typography.bodyMedium)

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        TextButton(
                            onClick = { if (quantity > 1) quantity-- },
                            enabled = quantity > 1,
                        ) { Text("−") }

                        Text("$quantity", style = MaterialTheme.typography.titleMedium)

                        TextButton(
                            onClick = { if (quantity < 20) quantity++ },
                            enabled = quantity < 20,
                        ) { Text("+") }
                    }

                    Button(
                        onClick = {
                            viewModel.addToBasket(quantity) { message ->
                                scope.launch { snackbarHostState.showSnackbar(message) }
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("Add to basket") }
                }
            }
        }
    }
}

// --- Basket ---------------------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BasketScreen(viewModel: BasketViewModel, onBack: () -> Unit, onCheckout: () -> Unit) {
    val basket by viewModel.basket.collectAsState()
    val busy by viewModel.busy.collectAsState()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Your basket") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        val current = basket

        if (current == null || current.items.isEmpty()) {
            Box(Modifier.padding(padding).fillMaxSize(), Alignment.Center) {
                Text("Your basket is empty.")
            }
            return@Scaffold
        }

        Column(Modifier.padding(padding).fillMaxSize()) {
            LazyColumn(
                Modifier.weight(1f),
                contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(current.items, key = { it.productId }) { line ->
                    Card(Modifier.fillMaxWidth()) {
                        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(line.name, style = MaterialTheme.typography.titleSmall)
                                Text(
                                    "${line.quantity} × ${formatMoney(line.unitPrice, line.currency)}",
                                    style = MaterialTheme.typography.bodySmall,
                                )
                            }

                            Text(formatMoney(line.lineTotal, line.currency))

                            TextButton(
                                onClick = { viewModel.remove(line.productId) },
                                enabled = !busy,
                            ) { Text("Remove") }
                        }
                    }
                }
            }

            Column(Modifier.padding(16.dp)) {
                Row(Modifier.fillMaxWidth(), Arrangement.SpaceBetween) {
                    Text("Subtotal", style = MaterialTheme.typography.titleMedium)
                    Text(
                        formatMoney(current.subtotal, current.currency),
                        style = MaterialTheme.typography.titleMedium,
                    )
                }

                Spacer(Modifier.height(12.dp))

                Button(
                    onClick = onCheckout,
                    enabled = !busy,
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Checkout") }
            }
        }
    }
}

// --- Checkout -----------------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CheckoutScreen(viewModel: CheckoutViewModel, onBack: () -> Unit, onPlaced: (String) -> Unit) {
    val state by viewModel.state.collectAsState()

    var line1 by remember { mutableStateOf("") }
    var city by remember { mutableStateOf("") }
    var postcode by remember { mutableStateOf("") }
    var country by remember { mutableStateOf("EG") }

    LaunchedEffect(state.placedOrderId) {
        state.placedOrderId?.let(onPlaced)
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Checkout") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        Column(
            Modifier.padding(padding).fillMaxSize().padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            OutlinedTextField(line1, { line1 = it }, label = { Text("Address") },
                singleLine = true, modifier = Modifier.fillMaxWidth())
            OutlinedTextField(city, { city = it }, label = { Text("City") },
                singleLine = true, modifier = Modifier.fillMaxWidth())
            OutlinedTextField(postcode, { postcode = it }, label = { Text("Postcode") },
                singleLine = true, modifier = Modifier.fillMaxWidth())
            OutlinedTextField(country, { country = it.uppercase().take(2) },
                label = { Text("Country (ISO code)") }, singleLine = true,
                modifier = Modifier.fillMaxWidth())

            state.error?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }

            Button(
                onClick = {
                    viewModel.placeOrder(ShippingAddress(line1.trim(), city.trim(), postcode.trim(), country))
                },
                enabled = !state.submitting &&
                    line1.isNotBlank() && city.isNotBlank() && postcode.isNotBlank() && country.length == 2,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(if (state.submitting) "Placing your order…" else "Place order")
            }
        }
    }
}

// --- Orders --------------------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OrdersScreen(
    viewModel: OrdersViewModel,
    onBack: () -> Unit,
    onOrderSelected: (OrderSummary) -> Unit,
) {
    val orders by viewModel.list.collectAsState()
    val loading by viewModel.loading.collectAsState()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Your orders") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        when {
            loading -> Box(Modifier.padding(padding).fillMaxSize(), Alignment.Center) {
                CircularProgressIndicator()
            }

            orders.isEmpty() -> Box(Modifier.padding(padding).fillMaxSize(), Alignment.Center) {
                Text("You have no orders yet.")
            }

            else -> LazyColumn(
                Modifier.padding(padding),
                contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(orders, key = { it.id }) { order ->
                    Card(
                        Modifier.fillMaxWidth().clickable { onOrderSelected(order) },
                    ) {
                        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(order.orderNumber, style = MaterialTheme.typography.titleSmall)
                                Text(
                                    "${order.lineCount} item(s) · ${orderStatusLabel(order.status)}",
                                    style = MaterialTheme.typography.bodySmall,
                                )
                            }
                            Text(formatMoney(order.total, order.currency))
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OrderDetailScreen(
    orderId: String,
    viewModel: OrdersViewModel,
    snackbarHostState: SnackbarHostState,
    onBack: () -> Unit,
) {
    val order by viewModel.detail.collectAsState()
    val scope = rememberCoroutineScope()

    LaunchedEffect(orderId) { viewModel.loadDetail(orderId) }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = { Text(order?.orderNumber ?: "Order") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        val detail = order

        if (detail == null) {
            Box(Modifier.padding(padding).fillMaxSize(), Alignment.Center) { CircularProgressIndicator() }
            return@Scaffold
        }

        Column(
            Modifier.padding(padding).fillMaxSize().padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Text(orderStatusLabel(detail.status), style = MaterialTheme.typography.titleMedium)

            if (detail.status == "Pending" || detail.status == "AwaitingPayment") {
                Text(
                    "We're confirming stock and payment. This updates on its own.",
                    style = MaterialTheme.typography.bodySmall,
                )
            }

            detail.cancellationReason?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }

            detail.lines.forEach { line ->
                Row(Modifier.fillMaxWidth(), Arrangement.SpaceBetween) {
                    Text("${line.quantity} × ${line.name}")
                    Text(formatMoney(line.lineTotal, detail.currency))
                }
            }

            Row(Modifier.fillMaxWidth(), Arrangement.SpaceBetween) {
                Text("Total", style = MaterialTheme.typography.titleMedium)
                Text(formatMoney(detail.total, detail.currency), style = MaterialTheme.typography.titleMedium)
            }

            if (detail.status != "Cancelled" && detail.status != "Shipped") {
                Button(
                    onClick = {
                        viewModel.cancel(detail.id) { message ->
                            scope.launch { snackbarHostState.showSnackbar(message) }
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Cancel order") }
            }
        }
    }
}

// --- Sign in ---------------------------------------------------------------------------------

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SignInScreen(viewModel: AuthViewModel, onSignedIn: () -> Unit, onBack: () -> Unit) {
    val state by viewModel.state.collectAsState()
    val profile by viewModel.profile.collectAsState()

    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var displayName by remember { mutableStateOf("") }
    var registering by remember { mutableStateOf(false) }

    LaunchedEffect(state.signedIn) {
        if (state.signedIn && profile != null) onSignedIn()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(if (registering) "Create an account" else "Sign in") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        if (state.signedIn && profile != null) {
            Column(
                Modifier.padding(padding).fillMaxSize().padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text("Signed in as ${profile!!.displayName}", style = MaterialTheme.typography.titleMedium)
                Button(onClick = viewModel::signOut, modifier = Modifier.fillMaxWidth()) {
                    Text("Sign out")
                }
            }
            return@Scaffold
        }

        Column(
            Modifier.padding(padding).fillMaxSize().padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (registering) {
                OutlinedTextField(displayName, { displayName = it }, label = { Text("Name") },
                    singleLine = true, modifier = Modifier.fillMaxWidth())
            }

            OutlinedTextField(
                email, { email = it }, label = { Text("Email") }, singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Email),
                modifier = Modifier.fillMaxWidth(),
            )

            OutlinedTextField(
                password, { password = it }, label = { Text("Password") }, singleLine = true,
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                modifier = Modifier.fillMaxWidth(),
            )

            if (registering) {
                Text(
                    "At least 12 characters, including a letter and a digit.",
                    style = MaterialTheme.typography.bodySmall,
                )
            }

            state.error?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }

            Button(
                onClick = {
                    if (registering) viewModel.register(email, password, displayName)
                    else viewModel.signIn(email, password)
                },
                enabled = !state.busy && email.isNotBlank() && password.isNotBlank() &&
                    (!registering || displayName.isNotBlank()),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(if (state.busy) "Please wait…" else if (registering) "Create account" else "Sign in")
            }

            TextButton(onClick = { registering = !registering }, modifier = Modifier.fillMaxWidth()) {
                Text(if (registering) "I already have an account" else "Create an account")
            }
        }
    }
}
