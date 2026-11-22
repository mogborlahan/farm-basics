import 'package:flutter_test/flutter_test.dart';

import 'package:farm_basics_app/main.dart';

void main() {
  testWidgets('FarmBasicsApp renders offline-ready Sprint 0 shell', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(const FarmBasicsApp());

    expect(find.text('Farm Basics'), findsOneWidget);
    expect(find.text('Offline-first outbox ready (WP-04)'), findsOneWidget);
  });
}
