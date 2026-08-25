package com.sama.shop

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.sama.shop.data.SessionEvents
import com.sama.shop.ui.screens.BasketScreen
import com.sama.shop.ui.screens.CatalogScreen
import com.sama.shop.ui.screens.CheckoutScreen
import com.sama.shop.ui.screens.OrderDetailScreen
import com.sama.shop.ui.screens.OrdersScreen
import com.sama.shop.ui.screens.ProductScreen
import com.sama.shop.ui.screens.SignInScreen
import com.sama.shop.ui.theme.SamaTheme
import dagger.hilt.android.AndroidEntryPoint
import javax.inject.Inject

object Routes {
    const val CATALOG = "catalog"
    const val PRODUCT = "product/{slug}"
    const val BASKET = "basket"
    const val CHECKOUT = "checkout"
    const val ORDERS = "orders"
    const val ORDER_DETAIL = "orders/{id}"
    const val SIGN_IN = "sign-in"

    fun product(slug: String) = "product/$slug"
    fun orderDetail(id: String) = "orders/$id"
}

@AndroidEntryPoint
class MainActivity : ComponentActivity() {

    @Inject
    lateinit var sessionEvents: SessionEvents

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        setContent {
            SamaTheme {
                val navController = rememberNavController()
                val snackbarHostState = remember { SnackbarHostState() }

                // The network layer discovers an expired session on a background thread; this
                // is where that becomes a navigation.
                LaunchedEffect(Unit) {
                    sessionEvents.sessionExpired.collect {
                        snackbarHostState.showSnackbar("Your session has expired. Please sign in again.")
                        navController.navigate(Routes.SIGN_IN) {
                            popUpTo(Routes.CATALOG)
                        }
                    }
                }

                SamaNavHost(navController, snackbarHostState)
            }
        }
    }
}

@Composable
private fun SamaNavHost(
    navController: NavHostController,
    snackbarHostState: SnackbarHostState,
) {
    NavHost(navController = navController, startDestination = Routes.CATALOG) {

        composable(Routes.CATALOG) {
            CatalogScreen(
                viewModel = hiltViewModel(),
                snackbarHostState = snackbarHostState,
                onProductSelected = { navController.navigate(Routes.product(it.slug)) },
                onOpenBasket = { navController.navigate(Routes.BASKET) },
                onOpenOrders = { navController.navigate(Routes.ORDERS) },
                onSignIn = { navController.navigate(Routes.SIGN_IN) },
            )
        }

        composable(Routes.PRODUCT) { entry ->
            ProductScreen(
                slug = entry.arguments?.getString("slug").orEmpty(),
                viewModel = hiltViewModel(),
                snackbarHostState = snackbarHostState,
                onBack = { navController.popBackStack() },
            )
        }

        composable(Routes.BASKET) {
            BasketScreen(
                viewModel = hiltViewModel(),
                onBack = { navController.popBackStack() },
                onCheckout = { navController.navigate(Routes.CHECKOUT) },
            )
        }

        composable(Routes.CHECKOUT) {
            CheckoutScreen(
                viewModel = hiltViewModel(),
                onBack = { navController.popBackStack() },
                onPlaced = { orderId ->
                    navController.navigate(Routes.orderDetail(orderId)) {
                        // Checkout must not remain on the back stack: returning to it would
                        // offer to place the order the customer has already placed.
                        popUpTo(Routes.CATALOG)
                    }
                },
            )
        }

        composable(Routes.ORDERS) {
            OrdersScreen(
                viewModel = hiltViewModel(),
                onBack = { navController.popBackStack() },
                onOrderSelected = { navController.navigate(Routes.orderDetail(it.id)) },
            )
        }

        composable(Routes.ORDER_DETAIL) { entry ->
            OrderDetailScreen(
                orderId = entry.arguments?.getString("id").orEmpty(),
                viewModel = hiltViewModel(),
                snackbarHostState = snackbarHostState,
                onBack = { navController.popBackStack() },
            )
        }

        composable(Routes.SIGN_IN) {
            SignInScreen(
                viewModel = hiltViewModel(),
                onSignedIn = { navController.popBackStack() },
                onBack = { navController.popBackStack() },
            )
        }
    }
}
