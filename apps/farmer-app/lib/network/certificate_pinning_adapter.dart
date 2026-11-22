import 'dart:convert';
import 'dart:io';

import 'package:crypto/crypto.dart';
import 'package:dio/io.dart';

/// Builds a fail-closed [IOHttpClientAdapter] with SHA-256 certificate pinning
/// aligned with Doc/01A-ENG D-14.
IOHttpClientAdapter createPinnedHttpClientAdapter({
  required Set<String> allowedSha256Pins,
  bool allowInsecureLocalhost = false,
}) {
  final Set<String> normalizedPins = allowedSha256Pins
      .map((String pin) => pin.trim().toLowerCase())
      .where((String pin) => pin.isNotEmpty)
      .toSet();

  return IOHttpClientAdapter(
    createHttpClient: () {
      final HttpClient client = HttpClient();
      // Reject any TLS chain that fails standard OS trust verification.
      client.badCertificateCallback =
          (X509Certificate cert, String host, int port) => false;
      return client;
    },
    validateCertificate: (X509Certificate? cert, String host, int port) {
      if (cert == null) {
        return allowInsecureLocalhost && _isLoopbackHost(host);
      }

      if (normalizedPins.isEmpty) {
        return false;
      }

      final Digest digest = sha256.convert(cert.der);
      final String hexDigest = digest.toString().toLowerCase();
      final String base64Digest = base64Encode(digest.bytes).toLowerCase();
      return normalizedPins.contains(hexDigest) ||
          normalizedPins.contains(base64Digest);
    },
  );
}

bool _isLoopbackHost(String host) {
  final String normalized = host.trim().toLowerCase();
  return normalized == 'localhost' ||
      normalized == '127.0.0.1' ||
      normalized == '::1';
}
