<?php

// Test script for the simplified /db/users endpoint
$apiUrl = 'https://win.aspiservizi.it/db-bridge/db/users';
$secret = 'dev-bridge-secret-2025';

$ch = curl_init($apiUrl);
curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
curl_setopt($ch, CURLOPT_HTTPHEADER, [
    'X-Bridge-Secret: ' . $secret
]);
curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false);
curl_setopt($ch, CURLOPT_TIMEOUT, 10); // 10 second timeout

$response = curl_exec($ch);
$httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
$error = curl_error($ch);
curl_close($ch);

echo "Testing endpoint: $apiUrl\n";
echo "HTTP Code: $httpCode\n";
echo "Response: $response\n";
if ($error) {
    echo "Error: $error\n";
}

?>