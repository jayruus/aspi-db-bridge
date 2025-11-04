<?php

/**
 * Test script for DbBridge users endpoint
 * Run this to get all users from the database
 */

// API endpoint
$apiUrl = 'https://win.aspiservizi.it/db-bridge/db/users';
$secret = 'dev-bridge-secret-2025';

// Initialize cURL
$ch = curl_init($apiUrl);

// Set cURL options
curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
curl_setopt($ch, CURLOPT_HTTPHEADER, [
    'X-Bridge-Secret: ' . $secret
]);
curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false); // For testing - remove in production
curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false); // For testing - remove in production

// Execute the request
$response = curl_exec($ch);
$httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
$error = curl_error($ch);

// Close cURL
curl_close($ch);

// Output results
echo "=== DbBridge Users Test ===\n";
echo "URL: $apiUrl\n";
echo "HTTP Status: $httpCode\n\n";

if ($error) {
    echo "cURL Error: $error\n";
} else {
    echo "Response:\n";
    $decoded = json_decode($response, true);
    if (json_last_error() === JSON_ERROR_NONE) {
        if (isset($decoded['data']) && is_array($decoded['data'])) {
            echo "Found " . count($decoded['data']) . " users:\n\n";
            foreach ($decoded['data'] as $user) {
                echo "Username: " . ($user['username'] ?? 'N/A') . "\n";
                echo "Password: " . ($user['password'] ?? 'N/A') . "\n";
                echo "Role: " . ($user['ruolo'] ?? 'N/A') . "\n";
                echo "Active: " . ($user['attivo'] ?? 'N/A') . "\n";
                echo "---\n";
            }
        } else {
            echo json_encode($decoded, JSON_PRETTY_PRINT) . "\n";
        }
    } else {
        echo $response . "\n";
    }
}

echo "\n=== Test Complete ===\n";

?>