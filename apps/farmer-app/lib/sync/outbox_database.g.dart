// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'outbox_database.dart';

// ignore_for_file: type=lint
class $LocalOutboxMutationsTable extends LocalOutboxMutations
    with TableInfo<$LocalOutboxMutationsTable, LocalOutboxMutation> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $LocalOutboxMutationsTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _seqIdMeta = const VerificationMeta('seqId');
  @override
  late final GeneratedColumn<int> seqId = GeneratedColumn<int>(
    'seq_id',
    aliasedName,
    false,
    hasAutoIncrement: true,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
    defaultConstraints: GeneratedColumn.constraintIsAlways(
      'PRIMARY KEY AUTOINCREMENT',
    ),
  );
  static const VerificationMeta _clientGuidMeta = const VerificationMeta(
    'clientGuid',
  );
  @override
  late final GeneratedColumn<String> clientGuid = GeneratedColumn<String>(
    'client_guid',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
    defaultConstraints: GeneratedColumn.constraintIsAlways('UNIQUE'),
  );
  static const VerificationMeta _entityTypeMeta = const VerificationMeta(
    'entityType',
  );
  @override
  late final GeneratedColumn<String> entityType = GeneratedColumn<String>(
    'entity_type',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _entityLocalIdMeta = const VerificationMeta(
    'entityLocalId',
  );
  @override
  late final GeneratedColumn<String> entityLocalId = GeneratedColumn<String>(
    'entity_local_id',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _parentEntityLocalIdMeta =
      const VerificationMeta('parentEntityLocalId');
  @override
  late final GeneratedColumn<String> parentEntityLocalId =
      GeneratedColumn<String>(
        'parent_entity_local_id',
        aliasedName,
        true,
        type: DriftSqlType.string,
        requiredDuringInsert: false,
      );
  static const VerificationMeta _httpMethodMeta = const VerificationMeta(
    'httpMethod',
  );
  @override
  late final GeneratedColumn<String> httpMethod = GeneratedColumn<String>(
    'http_method',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _endpointPathMeta = const VerificationMeta(
    'endpointPath',
  );
  @override
  late final GeneratedColumn<String> endpointPath = GeneratedColumn<String>(
    'endpoint_path',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _payloadJsonMeta = const VerificationMeta(
    'payloadJson',
  );
  @override
  late final GeneratedColumn<String> payloadJson = GeneratedColumn<String>(
    'payload_json',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _attachmentLocalPathsMeta =
      const VerificationMeta('attachmentLocalPaths');
  @override
  late final GeneratedColumn<String> attachmentLocalPaths =
      GeneratedColumn<String>(
        'attachment_local_paths',
        aliasedName,
        true,
        type: DriftSqlType.string,
        requiredDuringInsert: false,
      );
  static const VerificationMeta _clientRecordedAtMeta = const VerificationMeta(
    'clientRecordedAt',
  );
  @override
  late final GeneratedColumn<DateTime> clientRecordedAt =
      GeneratedColumn<DateTime>(
        'client_recorded_at',
        aliasedName,
        false,
        type: DriftSqlType.dateTime,
        requiredDuringInsert: true,
      );
  static const VerificationMeta _monotonicElapsedMsMeta =
      const VerificationMeta('monotonicElapsedMs');
  @override
  late final GeneratedColumn<int> monotonicElapsedMs = GeneratedColumn<int>(
    'monotonic_elapsed_ms',
    aliasedName,
    false,
    type: DriftSqlType.int,
    requiredDuringInsert: true,
  );
  static const VerificationMeta _actorIdentityIdMeta = const VerificationMeta(
    'actorIdentityId',
  );
  @override
  late final GeneratedColumn<String> actorIdentityId = GeneratedColumn<String>(
    'actor_identity_id',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _agentSessionIdMeta = const VerificationMeta(
    'agentSessionId',
  );
  @override
  late final GeneratedColumn<String> agentSessionId = GeneratedColumn<String>(
    'agent_session_id',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _fobIdUsedMeta = const VerificationMeta(
    'fobIdUsed',
  );
  @override
  late final GeneratedColumn<String> fobIdUsed = GeneratedColumn<String>(
    'fob_id_used',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _fobTappedAtMeta = const VerificationMeta(
    'fobTappedAt',
  );
  @override
  late final GeneratedColumn<DateTime> fobTappedAt = GeneratedColumn<DateTime>(
    'fob_tapped_at',
    aliasedName,
    true,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _syncStatusMeta = const VerificationMeta(
    'syncStatus',
  );
  @override
  late final GeneratedColumn<String> syncStatus = GeneratedColumn<String>(
    'sync_status',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant('pending'),
  );
  static const VerificationMeta _retryCountMeta = const VerificationMeta(
    'retryCount',
  );
  @override
  late final GeneratedColumn<int> retryCount = GeneratedColumn<int>(
    'retry_count',
    aliasedName,
    false,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
    defaultValue: const Constant(0),
  );
  static const VerificationMeta _nextRetryAtMeta = const VerificationMeta(
    'nextRetryAt',
  );
  @override
  late final GeneratedColumn<DateTime> nextRetryAt = GeneratedColumn<DateTime>(
    'next_retry_at',
    aliasedName,
    true,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _lastErrorCodeMeta = const VerificationMeta(
    'lastErrorCode',
  );
  @override
  late final GeneratedColumn<String> lastErrorCode = GeneratedColumn<String>(
    'last_error_code',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _serverReceivedAtMeta = const VerificationMeta(
    'serverReceivedAt',
  );
  @override
  late final GeneratedColumn<DateTime> serverReceivedAt =
      GeneratedColumn<DateTime>(
        'server_received_at',
        aliasedName,
        true,
        type: DriftSqlType.dateTime,
        requiredDuringInsert: false,
      );
  @override
  List<GeneratedColumn> get $columns => [
    seqId,
    clientGuid,
    entityType,
    entityLocalId,
    parentEntityLocalId,
    httpMethod,
    endpointPath,
    payloadJson,
    attachmentLocalPaths,
    clientRecordedAt,
    monotonicElapsedMs,
    actorIdentityId,
    agentSessionId,
    fobIdUsed,
    fobTappedAt,
    syncStatus,
    retryCount,
    nextRetryAt,
    lastErrorCode,
    serverReceivedAt,
  ];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'local_outbox_mutation';
  @override
  VerificationContext validateIntegrity(
    Insertable<LocalOutboxMutation> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('seq_id')) {
      context.handle(
        _seqIdMeta,
        seqId.isAcceptableOrUnknown(data['seq_id']!, _seqIdMeta),
      );
    }
    if (data.containsKey('client_guid')) {
      context.handle(
        _clientGuidMeta,
        clientGuid.isAcceptableOrUnknown(data['client_guid']!, _clientGuidMeta),
      );
    } else if (isInserting) {
      context.missing(_clientGuidMeta);
    }
    if (data.containsKey('entity_type')) {
      context.handle(
        _entityTypeMeta,
        entityType.isAcceptableOrUnknown(data['entity_type']!, _entityTypeMeta),
      );
    } else if (isInserting) {
      context.missing(_entityTypeMeta);
    }
    if (data.containsKey('entity_local_id')) {
      context.handle(
        _entityLocalIdMeta,
        entityLocalId.isAcceptableOrUnknown(
          data['entity_local_id']!,
          _entityLocalIdMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_entityLocalIdMeta);
    }
    if (data.containsKey('parent_entity_local_id')) {
      context.handle(
        _parentEntityLocalIdMeta,
        parentEntityLocalId.isAcceptableOrUnknown(
          data['parent_entity_local_id']!,
          _parentEntityLocalIdMeta,
        ),
      );
    }
    if (data.containsKey('http_method')) {
      context.handle(
        _httpMethodMeta,
        httpMethod.isAcceptableOrUnknown(data['http_method']!, _httpMethodMeta),
      );
    } else if (isInserting) {
      context.missing(_httpMethodMeta);
    }
    if (data.containsKey('endpoint_path')) {
      context.handle(
        _endpointPathMeta,
        endpointPath.isAcceptableOrUnknown(
          data['endpoint_path']!,
          _endpointPathMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_endpointPathMeta);
    }
    if (data.containsKey('payload_json')) {
      context.handle(
        _payloadJsonMeta,
        payloadJson.isAcceptableOrUnknown(
          data['payload_json']!,
          _payloadJsonMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_payloadJsonMeta);
    }
    if (data.containsKey('attachment_local_paths')) {
      context.handle(
        _attachmentLocalPathsMeta,
        attachmentLocalPaths.isAcceptableOrUnknown(
          data['attachment_local_paths']!,
          _attachmentLocalPathsMeta,
        ),
      );
    }
    if (data.containsKey('client_recorded_at')) {
      context.handle(
        _clientRecordedAtMeta,
        clientRecordedAt.isAcceptableOrUnknown(
          data['client_recorded_at']!,
          _clientRecordedAtMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_clientRecordedAtMeta);
    }
    if (data.containsKey('monotonic_elapsed_ms')) {
      context.handle(
        _monotonicElapsedMsMeta,
        monotonicElapsedMs.isAcceptableOrUnknown(
          data['monotonic_elapsed_ms']!,
          _monotonicElapsedMsMeta,
        ),
      );
    } else if (isInserting) {
      context.missing(_monotonicElapsedMsMeta);
    }
    if (data.containsKey('actor_identity_id')) {
      context.handle(
        _actorIdentityIdMeta,
        actorIdentityId.isAcceptableOrUnknown(
          data['actor_identity_id']!,
          _actorIdentityIdMeta,
        ),
      );
    }
    if (data.containsKey('agent_session_id')) {
      context.handle(
        _agentSessionIdMeta,
        agentSessionId.isAcceptableOrUnknown(
          data['agent_session_id']!,
          _agentSessionIdMeta,
        ),
      );
    }
    if (data.containsKey('fob_id_used')) {
      context.handle(
        _fobIdUsedMeta,
        fobIdUsed.isAcceptableOrUnknown(data['fob_id_used']!, _fobIdUsedMeta),
      );
    }
    if (data.containsKey('fob_tapped_at')) {
      context.handle(
        _fobTappedAtMeta,
        fobTappedAt.isAcceptableOrUnknown(
          data['fob_tapped_at']!,
          _fobTappedAtMeta,
        ),
      );
    }
    if (data.containsKey('sync_status')) {
      context.handle(
        _syncStatusMeta,
        syncStatus.isAcceptableOrUnknown(data['sync_status']!, _syncStatusMeta),
      );
    }
    if (data.containsKey('retry_count')) {
      context.handle(
        _retryCountMeta,
        retryCount.isAcceptableOrUnknown(data['retry_count']!, _retryCountMeta),
      );
    }
    if (data.containsKey('next_retry_at')) {
      context.handle(
        _nextRetryAtMeta,
        nextRetryAt.isAcceptableOrUnknown(
          data['next_retry_at']!,
          _nextRetryAtMeta,
        ),
      );
    }
    if (data.containsKey('last_error_code')) {
      context.handle(
        _lastErrorCodeMeta,
        lastErrorCode.isAcceptableOrUnknown(
          data['last_error_code']!,
          _lastErrorCodeMeta,
        ),
      );
    }
    if (data.containsKey('server_received_at')) {
      context.handle(
        _serverReceivedAtMeta,
        serverReceivedAt.isAcceptableOrUnknown(
          data['server_received_at']!,
          _serverReceivedAtMeta,
        ),
      );
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {seqId};
  @override
  LocalOutboxMutation map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return LocalOutboxMutation(
      seqId: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}seq_id'],
      )!,
      clientGuid: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}client_guid'],
      )!,
      entityType: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}entity_type'],
      )!,
      entityLocalId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}entity_local_id'],
      )!,
      parentEntityLocalId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}parent_entity_local_id'],
      ),
      httpMethod: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}http_method'],
      )!,
      endpointPath: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}endpoint_path'],
      )!,
      payloadJson: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}payload_json'],
      )!,
      attachmentLocalPaths: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}attachment_local_paths'],
      ),
      clientRecordedAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}client_recorded_at'],
      )!,
      monotonicElapsedMs: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}monotonic_elapsed_ms'],
      )!,
      actorIdentityId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}actor_identity_id'],
      ),
      agentSessionId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}agent_session_id'],
      ),
      fobIdUsed: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}fob_id_used'],
      ),
      fobTappedAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}fob_tapped_at'],
      ),
      syncStatus: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}sync_status'],
      )!,
      retryCount: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}retry_count'],
      )!,
      nextRetryAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}next_retry_at'],
      ),
      lastErrorCode: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}last_error_code'],
      ),
      serverReceivedAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}server_received_at'],
      ),
    );
  }

  @override
  $LocalOutboxMutationsTable createAlias(String alias) {
    return $LocalOutboxMutationsTable(attachedDatabase, alias);
  }
}

class LocalOutboxMutation extends DataClass
    implements Insertable<LocalOutboxMutation> {
  final int seqId;
  final String clientGuid;
  final String entityType;
  final String entityLocalId;
  final String? parentEntityLocalId;
  final String httpMethod;
  final String endpointPath;
  final String payloadJson;
  final String? attachmentLocalPaths;
  final DateTime clientRecordedAt;
  final int monotonicElapsedMs;
  final String? actorIdentityId;
  final String? agentSessionId;
  final String? fobIdUsed;
  final DateTime? fobTappedAt;
  final String syncStatus;
  final int retryCount;
  final DateTime? nextRetryAt;
  final String? lastErrorCode;
  final DateTime? serverReceivedAt;
  const LocalOutboxMutation({
    required this.seqId,
    required this.clientGuid,
    required this.entityType,
    required this.entityLocalId,
    this.parentEntityLocalId,
    required this.httpMethod,
    required this.endpointPath,
    required this.payloadJson,
    this.attachmentLocalPaths,
    required this.clientRecordedAt,
    required this.monotonicElapsedMs,
    this.actorIdentityId,
    this.agentSessionId,
    this.fobIdUsed,
    this.fobTappedAt,
    required this.syncStatus,
    required this.retryCount,
    this.nextRetryAt,
    this.lastErrorCode,
    this.serverReceivedAt,
  });
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['seq_id'] = Variable<int>(seqId);
    map['client_guid'] = Variable<String>(clientGuid);
    map['entity_type'] = Variable<String>(entityType);
    map['entity_local_id'] = Variable<String>(entityLocalId);
    if (!nullToAbsent || parentEntityLocalId != null) {
      map['parent_entity_local_id'] = Variable<String>(parentEntityLocalId);
    }
    map['http_method'] = Variable<String>(httpMethod);
    map['endpoint_path'] = Variable<String>(endpointPath);
    map['payload_json'] = Variable<String>(payloadJson);
    if (!nullToAbsent || attachmentLocalPaths != null) {
      map['attachment_local_paths'] = Variable<String>(attachmentLocalPaths);
    }
    map['client_recorded_at'] = Variable<DateTime>(clientRecordedAt);
    map['monotonic_elapsed_ms'] = Variable<int>(monotonicElapsedMs);
    if (!nullToAbsent || actorIdentityId != null) {
      map['actor_identity_id'] = Variable<String>(actorIdentityId);
    }
    if (!nullToAbsent || agentSessionId != null) {
      map['agent_session_id'] = Variable<String>(agentSessionId);
    }
    if (!nullToAbsent || fobIdUsed != null) {
      map['fob_id_used'] = Variable<String>(fobIdUsed);
    }
    if (!nullToAbsent || fobTappedAt != null) {
      map['fob_tapped_at'] = Variable<DateTime>(fobTappedAt);
    }
    map['sync_status'] = Variable<String>(syncStatus);
    map['retry_count'] = Variable<int>(retryCount);
    if (!nullToAbsent || nextRetryAt != null) {
      map['next_retry_at'] = Variable<DateTime>(nextRetryAt);
    }
    if (!nullToAbsent || lastErrorCode != null) {
      map['last_error_code'] = Variable<String>(lastErrorCode);
    }
    if (!nullToAbsent || serverReceivedAt != null) {
      map['server_received_at'] = Variable<DateTime>(serverReceivedAt);
    }
    return map;
  }

  LocalOutboxMutationsCompanion toCompanion(bool nullToAbsent) {
    return LocalOutboxMutationsCompanion(
      seqId: Value(seqId),
      clientGuid: Value(clientGuid),
      entityType: Value(entityType),
      entityLocalId: Value(entityLocalId),
      parentEntityLocalId: parentEntityLocalId == null && nullToAbsent
          ? const Value.absent()
          : Value(parentEntityLocalId),
      httpMethod: Value(httpMethod),
      endpointPath: Value(endpointPath),
      payloadJson: Value(payloadJson),
      attachmentLocalPaths: attachmentLocalPaths == null && nullToAbsent
          ? const Value.absent()
          : Value(attachmentLocalPaths),
      clientRecordedAt: Value(clientRecordedAt),
      monotonicElapsedMs: Value(monotonicElapsedMs),
      actorIdentityId: actorIdentityId == null && nullToAbsent
          ? const Value.absent()
          : Value(actorIdentityId),
      agentSessionId: agentSessionId == null && nullToAbsent
          ? const Value.absent()
          : Value(agentSessionId),
      fobIdUsed: fobIdUsed == null && nullToAbsent
          ? const Value.absent()
          : Value(fobIdUsed),
      fobTappedAt: fobTappedAt == null && nullToAbsent
          ? const Value.absent()
          : Value(fobTappedAt),
      syncStatus: Value(syncStatus),
      retryCount: Value(retryCount),
      nextRetryAt: nextRetryAt == null && nullToAbsent
          ? const Value.absent()
          : Value(nextRetryAt),
      lastErrorCode: lastErrorCode == null && nullToAbsent
          ? const Value.absent()
          : Value(lastErrorCode),
      serverReceivedAt: serverReceivedAt == null && nullToAbsent
          ? const Value.absent()
          : Value(serverReceivedAt),
    );
  }

  factory LocalOutboxMutation.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return LocalOutboxMutation(
      seqId: serializer.fromJson<int>(json['seqId']),
      clientGuid: serializer.fromJson<String>(json['clientGuid']),
      entityType: serializer.fromJson<String>(json['entityType']),
      entityLocalId: serializer.fromJson<String>(json['entityLocalId']),
      parentEntityLocalId: serializer.fromJson<String?>(
        json['parentEntityLocalId'],
      ),
      httpMethod: serializer.fromJson<String>(json['httpMethod']),
      endpointPath: serializer.fromJson<String>(json['endpointPath']),
      payloadJson: serializer.fromJson<String>(json['payloadJson']),
      attachmentLocalPaths: serializer.fromJson<String?>(
        json['attachmentLocalPaths'],
      ),
      clientRecordedAt: serializer.fromJson<DateTime>(json['clientRecordedAt']),
      monotonicElapsedMs: serializer.fromJson<int>(json['monotonicElapsedMs']),
      actorIdentityId: serializer.fromJson<String?>(json['actorIdentityId']),
      agentSessionId: serializer.fromJson<String?>(json['agentSessionId']),
      fobIdUsed: serializer.fromJson<String?>(json['fobIdUsed']),
      fobTappedAt: serializer.fromJson<DateTime?>(json['fobTappedAt']),
      syncStatus: serializer.fromJson<String>(json['syncStatus']),
      retryCount: serializer.fromJson<int>(json['retryCount']),
      nextRetryAt: serializer.fromJson<DateTime?>(json['nextRetryAt']),
      lastErrorCode: serializer.fromJson<String?>(json['lastErrorCode']),
      serverReceivedAt: serializer.fromJson<DateTime?>(
        json['serverReceivedAt'],
      ),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'seqId': serializer.toJson<int>(seqId),
      'clientGuid': serializer.toJson<String>(clientGuid),
      'entityType': serializer.toJson<String>(entityType),
      'entityLocalId': serializer.toJson<String>(entityLocalId),
      'parentEntityLocalId': serializer.toJson<String?>(parentEntityLocalId),
      'httpMethod': serializer.toJson<String>(httpMethod),
      'endpointPath': serializer.toJson<String>(endpointPath),
      'payloadJson': serializer.toJson<String>(payloadJson),
      'attachmentLocalPaths': serializer.toJson<String?>(attachmentLocalPaths),
      'clientRecordedAt': serializer.toJson<DateTime>(clientRecordedAt),
      'monotonicElapsedMs': serializer.toJson<int>(monotonicElapsedMs),
      'actorIdentityId': serializer.toJson<String?>(actorIdentityId),
      'agentSessionId': serializer.toJson<String?>(agentSessionId),
      'fobIdUsed': serializer.toJson<String?>(fobIdUsed),
      'fobTappedAt': serializer.toJson<DateTime?>(fobTappedAt),
      'syncStatus': serializer.toJson<String>(syncStatus),
      'retryCount': serializer.toJson<int>(retryCount),
      'nextRetryAt': serializer.toJson<DateTime?>(nextRetryAt),
      'lastErrorCode': serializer.toJson<String?>(lastErrorCode),
      'serverReceivedAt': serializer.toJson<DateTime?>(serverReceivedAt),
    };
  }

  LocalOutboxMutation copyWith({
    int? seqId,
    String? clientGuid,
    String? entityType,
    String? entityLocalId,
    Value<String?> parentEntityLocalId = const Value.absent(),
    String? httpMethod,
    String? endpointPath,
    String? payloadJson,
    Value<String?> attachmentLocalPaths = const Value.absent(),
    DateTime? clientRecordedAt,
    int? monotonicElapsedMs,
    Value<String?> actorIdentityId = const Value.absent(),
    Value<String?> agentSessionId = const Value.absent(),
    Value<String?> fobIdUsed = const Value.absent(),
    Value<DateTime?> fobTappedAt = const Value.absent(),
    String? syncStatus,
    int? retryCount,
    Value<DateTime?> nextRetryAt = const Value.absent(),
    Value<String?> lastErrorCode = const Value.absent(),
    Value<DateTime?> serverReceivedAt = const Value.absent(),
  }) => LocalOutboxMutation(
    seqId: seqId ?? this.seqId,
    clientGuid: clientGuid ?? this.clientGuid,
    entityType: entityType ?? this.entityType,
    entityLocalId: entityLocalId ?? this.entityLocalId,
    parentEntityLocalId: parentEntityLocalId.present
        ? parentEntityLocalId.value
        : this.parentEntityLocalId,
    httpMethod: httpMethod ?? this.httpMethod,
    endpointPath: endpointPath ?? this.endpointPath,
    payloadJson: payloadJson ?? this.payloadJson,
    attachmentLocalPaths: attachmentLocalPaths.present
        ? attachmentLocalPaths.value
        : this.attachmentLocalPaths,
    clientRecordedAt: clientRecordedAt ?? this.clientRecordedAt,
    monotonicElapsedMs: monotonicElapsedMs ?? this.monotonicElapsedMs,
    actorIdentityId: actorIdentityId.present
        ? actorIdentityId.value
        : this.actorIdentityId,
    agentSessionId: agentSessionId.present
        ? agentSessionId.value
        : this.agentSessionId,
    fobIdUsed: fobIdUsed.present ? fobIdUsed.value : this.fobIdUsed,
    fobTappedAt: fobTappedAt.present ? fobTappedAt.value : this.fobTappedAt,
    syncStatus: syncStatus ?? this.syncStatus,
    retryCount: retryCount ?? this.retryCount,
    nextRetryAt: nextRetryAt.present ? nextRetryAt.value : this.nextRetryAt,
    lastErrorCode: lastErrorCode.present
        ? lastErrorCode.value
        : this.lastErrorCode,
    serverReceivedAt: serverReceivedAt.present
        ? serverReceivedAt.value
        : this.serverReceivedAt,
  );
  LocalOutboxMutation copyWithCompanion(LocalOutboxMutationsCompanion data) {
    return LocalOutboxMutation(
      seqId: data.seqId.present ? data.seqId.value : this.seqId,
      clientGuid: data.clientGuid.present
          ? data.clientGuid.value
          : this.clientGuid,
      entityType: data.entityType.present
          ? data.entityType.value
          : this.entityType,
      entityLocalId: data.entityLocalId.present
          ? data.entityLocalId.value
          : this.entityLocalId,
      parentEntityLocalId: data.parentEntityLocalId.present
          ? data.parentEntityLocalId.value
          : this.parentEntityLocalId,
      httpMethod: data.httpMethod.present
          ? data.httpMethod.value
          : this.httpMethod,
      endpointPath: data.endpointPath.present
          ? data.endpointPath.value
          : this.endpointPath,
      payloadJson: data.payloadJson.present
          ? data.payloadJson.value
          : this.payloadJson,
      attachmentLocalPaths: data.attachmentLocalPaths.present
          ? data.attachmentLocalPaths.value
          : this.attachmentLocalPaths,
      clientRecordedAt: data.clientRecordedAt.present
          ? data.clientRecordedAt.value
          : this.clientRecordedAt,
      monotonicElapsedMs: data.monotonicElapsedMs.present
          ? data.monotonicElapsedMs.value
          : this.monotonicElapsedMs,
      actorIdentityId: data.actorIdentityId.present
          ? data.actorIdentityId.value
          : this.actorIdentityId,
      agentSessionId: data.agentSessionId.present
          ? data.agentSessionId.value
          : this.agentSessionId,
      fobIdUsed: data.fobIdUsed.present ? data.fobIdUsed.value : this.fobIdUsed,
      fobTappedAt: data.fobTappedAt.present
          ? data.fobTappedAt.value
          : this.fobTappedAt,
      syncStatus: data.syncStatus.present
          ? data.syncStatus.value
          : this.syncStatus,
      retryCount: data.retryCount.present
          ? data.retryCount.value
          : this.retryCount,
      nextRetryAt: data.nextRetryAt.present
          ? data.nextRetryAt.value
          : this.nextRetryAt,
      lastErrorCode: data.lastErrorCode.present
          ? data.lastErrorCode.value
          : this.lastErrorCode,
      serverReceivedAt: data.serverReceivedAt.present
          ? data.serverReceivedAt.value
          : this.serverReceivedAt,
    );
  }

  @override
  String toString() {
    return (StringBuffer('LocalOutboxMutation(')
          ..write('seqId: $seqId, ')
          ..write('clientGuid: $clientGuid, ')
          ..write('entityType: $entityType, ')
          ..write('entityLocalId: $entityLocalId, ')
          ..write('parentEntityLocalId: $parentEntityLocalId, ')
          ..write('httpMethod: $httpMethod, ')
          ..write('endpointPath: $endpointPath, ')
          ..write('payloadJson: $payloadJson, ')
          ..write('attachmentLocalPaths: $attachmentLocalPaths, ')
          ..write('clientRecordedAt: $clientRecordedAt, ')
          ..write('monotonicElapsedMs: $monotonicElapsedMs, ')
          ..write('actorIdentityId: $actorIdentityId, ')
          ..write('agentSessionId: $agentSessionId, ')
          ..write('fobIdUsed: $fobIdUsed, ')
          ..write('fobTappedAt: $fobTappedAt, ')
          ..write('syncStatus: $syncStatus, ')
          ..write('retryCount: $retryCount, ')
          ..write('nextRetryAt: $nextRetryAt, ')
          ..write('lastErrorCode: $lastErrorCode, ')
          ..write('serverReceivedAt: $serverReceivedAt')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(
    seqId,
    clientGuid,
    entityType,
    entityLocalId,
    parentEntityLocalId,
    httpMethod,
    endpointPath,
    payloadJson,
    attachmentLocalPaths,
    clientRecordedAt,
    monotonicElapsedMs,
    actorIdentityId,
    agentSessionId,
    fobIdUsed,
    fobTappedAt,
    syncStatus,
    retryCount,
    nextRetryAt,
    lastErrorCode,
    serverReceivedAt,
  );
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is LocalOutboxMutation &&
          other.seqId == this.seqId &&
          other.clientGuid == this.clientGuid &&
          other.entityType == this.entityType &&
          other.entityLocalId == this.entityLocalId &&
          other.parentEntityLocalId == this.parentEntityLocalId &&
          other.httpMethod == this.httpMethod &&
          other.endpointPath == this.endpointPath &&
          other.payloadJson == this.payloadJson &&
          other.attachmentLocalPaths == this.attachmentLocalPaths &&
          other.clientRecordedAt == this.clientRecordedAt &&
          other.monotonicElapsedMs == this.monotonicElapsedMs &&
          other.actorIdentityId == this.actorIdentityId &&
          other.agentSessionId == this.agentSessionId &&
          other.fobIdUsed == this.fobIdUsed &&
          other.fobTappedAt == this.fobTappedAt &&
          other.syncStatus == this.syncStatus &&
          other.retryCount == this.retryCount &&
          other.nextRetryAt == this.nextRetryAt &&
          other.lastErrorCode == this.lastErrorCode &&
          other.serverReceivedAt == this.serverReceivedAt);
}

class LocalOutboxMutationsCompanion
    extends UpdateCompanion<LocalOutboxMutation> {
  final Value<int> seqId;
  final Value<String> clientGuid;
  final Value<String> entityType;
  final Value<String> entityLocalId;
  final Value<String?> parentEntityLocalId;
  final Value<String> httpMethod;
  final Value<String> endpointPath;
  final Value<String> payloadJson;
  final Value<String?> attachmentLocalPaths;
  final Value<DateTime> clientRecordedAt;
  final Value<int> monotonicElapsedMs;
  final Value<String?> actorIdentityId;
  final Value<String?> agentSessionId;
  final Value<String?> fobIdUsed;
  final Value<DateTime?> fobTappedAt;
  final Value<String> syncStatus;
  final Value<int> retryCount;
  final Value<DateTime?> nextRetryAt;
  final Value<String?> lastErrorCode;
  final Value<DateTime?> serverReceivedAt;
  const LocalOutboxMutationsCompanion({
    this.seqId = const Value.absent(),
    this.clientGuid = const Value.absent(),
    this.entityType = const Value.absent(),
    this.entityLocalId = const Value.absent(),
    this.parentEntityLocalId = const Value.absent(),
    this.httpMethod = const Value.absent(),
    this.endpointPath = const Value.absent(),
    this.payloadJson = const Value.absent(),
    this.attachmentLocalPaths = const Value.absent(),
    this.clientRecordedAt = const Value.absent(),
    this.monotonicElapsedMs = const Value.absent(),
    this.actorIdentityId = const Value.absent(),
    this.agentSessionId = const Value.absent(),
    this.fobIdUsed = const Value.absent(),
    this.fobTappedAt = const Value.absent(),
    this.syncStatus = const Value.absent(),
    this.retryCount = const Value.absent(),
    this.nextRetryAt = const Value.absent(),
    this.lastErrorCode = const Value.absent(),
    this.serverReceivedAt = const Value.absent(),
  });
  LocalOutboxMutationsCompanion.insert({
    this.seqId = const Value.absent(),
    required String clientGuid,
    required String entityType,
    required String entityLocalId,
    this.parentEntityLocalId = const Value.absent(),
    required String httpMethod,
    required String endpointPath,
    required String payloadJson,
    this.attachmentLocalPaths = const Value.absent(),
    required DateTime clientRecordedAt,
    required int monotonicElapsedMs,
    this.actorIdentityId = const Value.absent(),
    this.agentSessionId = const Value.absent(),
    this.fobIdUsed = const Value.absent(),
    this.fobTappedAt = const Value.absent(),
    this.syncStatus = const Value.absent(),
    this.retryCount = const Value.absent(),
    this.nextRetryAt = const Value.absent(),
    this.lastErrorCode = const Value.absent(),
    this.serverReceivedAt = const Value.absent(),
  }) : clientGuid = Value(clientGuid),
       entityType = Value(entityType),
       entityLocalId = Value(entityLocalId),
       httpMethod = Value(httpMethod),
       endpointPath = Value(endpointPath),
       payloadJson = Value(payloadJson),
       clientRecordedAt = Value(clientRecordedAt),
       monotonicElapsedMs = Value(monotonicElapsedMs);
  static Insertable<LocalOutboxMutation> custom({
    Expression<int>? seqId,
    Expression<String>? clientGuid,
    Expression<String>? entityType,
    Expression<String>? entityLocalId,
    Expression<String>? parentEntityLocalId,
    Expression<String>? httpMethod,
    Expression<String>? endpointPath,
    Expression<String>? payloadJson,
    Expression<String>? attachmentLocalPaths,
    Expression<DateTime>? clientRecordedAt,
    Expression<int>? monotonicElapsedMs,
    Expression<String>? actorIdentityId,
    Expression<String>? agentSessionId,
    Expression<String>? fobIdUsed,
    Expression<DateTime>? fobTappedAt,
    Expression<String>? syncStatus,
    Expression<int>? retryCount,
    Expression<DateTime>? nextRetryAt,
    Expression<String>? lastErrorCode,
    Expression<DateTime>? serverReceivedAt,
  }) {
    return RawValuesInsertable({
      if (seqId != null) 'seq_id': seqId,
      if (clientGuid != null) 'client_guid': clientGuid,
      if (entityType != null) 'entity_type': entityType,
      if (entityLocalId != null) 'entity_local_id': entityLocalId,
      if (parentEntityLocalId != null)
        'parent_entity_local_id': parentEntityLocalId,
      if (httpMethod != null) 'http_method': httpMethod,
      if (endpointPath != null) 'endpoint_path': endpointPath,
      if (payloadJson != null) 'payload_json': payloadJson,
      if (attachmentLocalPaths != null)
        'attachment_local_paths': attachmentLocalPaths,
      if (clientRecordedAt != null) 'client_recorded_at': clientRecordedAt,
      if (monotonicElapsedMs != null)
        'monotonic_elapsed_ms': monotonicElapsedMs,
      if (actorIdentityId != null) 'actor_identity_id': actorIdentityId,
      if (agentSessionId != null) 'agent_session_id': agentSessionId,
      if (fobIdUsed != null) 'fob_id_used': fobIdUsed,
      if (fobTappedAt != null) 'fob_tapped_at': fobTappedAt,
      if (syncStatus != null) 'sync_status': syncStatus,
      if (retryCount != null) 'retry_count': retryCount,
      if (nextRetryAt != null) 'next_retry_at': nextRetryAt,
      if (lastErrorCode != null) 'last_error_code': lastErrorCode,
      if (serverReceivedAt != null) 'server_received_at': serverReceivedAt,
    });
  }

  LocalOutboxMutationsCompanion copyWith({
    Value<int>? seqId,
    Value<String>? clientGuid,
    Value<String>? entityType,
    Value<String>? entityLocalId,
    Value<String?>? parentEntityLocalId,
    Value<String>? httpMethod,
    Value<String>? endpointPath,
    Value<String>? payloadJson,
    Value<String?>? attachmentLocalPaths,
    Value<DateTime>? clientRecordedAt,
    Value<int>? monotonicElapsedMs,
    Value<String?>? actorIdentityId,
    Value<String?>? agentSessionId,
    Value<String?>? fobIdUsed,
    Value<DateTime?>? fobTappedAt,
    Value<String>? syncStatus,
    Value<int>? retryCount,
    Value<DateTime?>? nextRetryAt,
    Value<String?>? lastErrorCode,
    Value<DateTime?>? serverReceivedAt,
  }) {
    return LocalOutboxMutationsCompanion(
      seqId: seqId ?? this.seqId,
      clientGuid: clientGuid ?? this.clientGuid,
      entityType: entityType ?? this.entityType,
      entityLocalId: entityLocalId ?? this.entityLocalId,
      parentEntityLocalId: parentEntityLocalId ?? this.parentEntityLocalId,
      httpMethod: httpMethod ?? this.httpMethod,
      endpointPath: endpointPath ?? this.endpointPath,
      payloadJson: payloadJson ?? this.payloadJson,
      attachmentLocalPaths: attachmentLocalPaths ?? this.attachmentLocalPaths,
      clientRecordedAt: clientRecordedAt ?? this.clientRecordedAt,
      monotonicElapsedMs: monotonicElapsedMs ?? this.monotonicElapsedMs,
      actorIdentityId: actorIdentityId ?? this.actorIdentityId,
      agentSessionId: agentSessionId ?? this.agentSessionId,
      fobIdUsed: fobIdUsed ?? this.fobIdUsed,
      fobTappedAt: fobTappedAt ?? this.fobTappedAt,
      syncStatus: syncStatus ?? this.syncStatus,
      retryCount: retryCount ?? this.retryCount,
      nextRetryAt: nextRetryAt ?? this.nextRetryAt,
      lastErrorCode: lastErrorCode ?? this.lastErrorCode,
      serverReceivedAt: serverReceivedAt ?? this.serverReceivedAt,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (seqId.present) {
      map['seq_id'] = Variable<int>(seqId.value);
    }
    if (clientGuid.present) {
      map['client_guid'] = Variable<String>(clientGuid.value);
    }
    if (entityType.present) {
      map['entity_type'] = Variable<String>(entityType.value);
    }
    if (entityLocalId.present) {
      map['entity_local_id'] = Variable<String>(entityLocalId.value);
    }
    if (parentEntityLocalId.present) {
      map['parent_entity_local_id'] = Variable<String>(
        parentEntityLocalId.value,
      );
    }
    if (httpMethod.present) {
      map['http_method'] = Variable<String>(httpMethod.value);
    }
    if (endpointPath.present) {
      map['endpoint_path'] = Variable<String>(endpointPath.value);
    }
    if (payloadJson.present) {
      map['payload_json'] = Variable<String>(payloadJson.value);
    }
    if (attachmentLocalPaths.present) {
      map['attachment_local_paths'] = Variable<String>(
        attachmentLocalPaths.value,
      );
    }
    if (clientRecordedAt.present) {
      map['client_recorded_at'] = Variable<DateTime>(clientRecordedAt.value);
    }
    if (monotonicElapsedMs.present) {
      map['monotonic_elapsed_ms'] = Variable<int>(monotonicElapsedMs.value);
    }
    if (actorIdentityId.present) {
      map['actor_identity_id'] = Variable<String>(actorIdentityId.value);
    }
    if (agentSessionId.present) {
      map['agent_session_id'] = Variable<String>(agentSessionId.value);
    }
    if (fobIdUsed.present) {
      map['fob_id_used'] = Variable<String>(fobIdUsed.value);
    }
    if (fobTappedAt.present) {
      map['fob_tapped_at'] = Variable<DateTime>(fobTappedAt.value);
    }
    if (syncStatus.present) {
      map['sync_status'] = Variable<String>(syncStatus.value);
    }
    if (retryCount.present) {
      map['retry_count'] = Variable<int>(retryCount.value);
    }
    if (nextRetryAt.present) {
      map['next_retry_at'] = Variable<DateTime>(nextRetryAt.value);
    }
    if (lastErrorCode.present) {
      map['last_error_code'] = Variable<String>(lastErrorCode.value);
    }
    if (serverReceivedAt.present) {
      map['server_received_at'] = Variable<DateTime>(serverReceivedAt.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('LocalOutboxMutationsCompanion(')
          ..write('seqId: $seqId, ')
          ..write('clientGuid: $clientGuid, ')
          ..write('entityType: $entityType, ')
          ..write('entityLocalId: $entityLocalId, ')
          ..write('parentEntityLocalId: $parentEntityLocalId, ')
          ..write('httpMethod: $httpMethod, ')
          ..write('endpointPath: $endpointPath, ')
          ..write('payloadJson: $payloadJson, ')
          ..write('attachmentLocalPaths: $attachmentLocalPaths, ')
          ..write('clientRecordedAt: $clientRecordedAt, ')
          ..write('monotonicElapsedMs: $monotonicElapsedMs, ')
          ..write('actorIdentityId: $actorIdentityId, ')
          ..write('agentSessionId: $agentSessionId, ')
          ..write('fobIdUsed: $fobIdUsed, ')
          ..write('fobTappedAt: $fobTappedAt, ')
          ..write('syncStatus: $syncStatus, ')
          ..write('retryCount: $retryCount, ')
          ..write('nextRetryAt: $nextRetryAt, ')
          ..write('lastErrorCode: $lastErrorCode, ')
          ..write('serverReceivedAt: $serverReceivedAt')
          ..write(')'))
        .toString();
  }
}

class $RegistrationDraftsTable extends RegistrationDrafts
    with TableInfo<$RegistrationDraftsTable, RegistrationDraft> {
  @override
  final GeneratedDatabase attachedDatabase;
  final String? _alias;
  $RegistrationDraftsTable(this.attachedDatabase, [this._alias]);
  static const VerificationMeta _idMeta = const VerificationMeta('id');
  @override
  late final GeneratedColumn<int> id = GeneratedColumn<int>(
    'id',
    aliasedName,
    false,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _stepIndexMeta = const VerificationMeta(
    'stepIndex',
  );
  @override
  late final GeneratedColumn<int> stepIndex = GeneratedColumn<int>(
    'step_index',
    aliasedName,
    false,
    type: DriftSqlType.int,
    requiredDuringInsert: false,
    defaultValue: const Constant(0),
  );
  static const VerificationMeta _phoneMeta = const VerificationMeta('phone');
  @override
  late final GeneratedColumn<String> phone = GeneratedColumn<String>(
    'phone',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant(''),
  );
  static const VerificationMeta _emailMeta = const VerificationMeta('email');
  @override
  late final GeneratedColumn<String> email = GeneratedColumn<String>(
    'email',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant(''),
  );
  static const VerificationMeta _stateCodeMeta = const VerificationMeta(
    'stateCode',
  );
  @override
  late final GeneratedColumn<String> stateCode = GeneratedColumn<String>(
    'state_code',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant(''),
  );
  static const VerificationMeta _lgaNameMeta = const VerificationMeta(
    'lgaName',
  );
  @override
  late final GeneratedColumn<String> lgaName = GeneratedColumn<String>(
    'lga_name',
    aliasedName,
    false,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
    defaultValue: const Constant(''),
  );
  static const VerificationMeta _pendingIdentityIdMeta = const VerificationMeta(
    'pendingIdentityId',
  );
  @override
  late final GeneratedColumn<String> pendingIdentityId =
      GeneratedColumn<String>(
        'pending_identity_id',
        aliasedName,
        true,
        type: DriftSqlType.string,
        requiredDuringInsert: false,
      );
  static const VerificationMeta _farmerIdMeta = const VerificationMeta(
    'farmerId',
  );
  @override
  late final GeneratedColumn<String> farmerId = GeneratedColumn<String>(
    'farmer_id',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _statusMessageMeta = const VerificationMeta(
    'statusMessage',
  );
  @override
  late final GeneratedColumn<String> statusMessage = GeneratedColumn<String>(
    'status_message',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _lastErrorCodeMeta = const VerificationMeta(
    'lastErrorCode',
  );
  @override
  late final GeneratedColumn<String> lastErrorCode = GeneratedColumn<String>(
    'last_error_code',
    aliasedName,
    true,
    type: DriftSqlType.string,
    requiredDuringInsert: false,
  );
  static const VerificationMeta _lockoutUntilUtcMeta = const VerificationMeta(
    'lockoutUntilUtc',
  );
  @override
  late final GeneratedColumn<DateTime> lockoutUntilUtc =
      GeneratedColumn<DateTime>(
        'lockout_until_utc',
        aliasedName,
        true,
        type: DriftSqlType.dateTime,
        requiredDuringInsert: false,
      );
  static const VerificationMeta _updatedAtMeta = const VerificationMeta(
    'updatedAt',
  );
  @override
  late final GeneratedColumn<DateTime> updatedAt = GeneratedColumn<DateTime>(
    'updated_at',
    aliasedName,
    false,
    type: DriftSqlType.dateTime,
    requiredDuringInsert: true,
  );
  @override
  List<GeneratedColumn> get $columns => [
    id,
    stepIndex,
    phone,
    email,
    stateCode,
    lgaName,
    pendingIdentityId,
    farmerId,
    statusMessage,
    lastErrorCode,
    lockoutUntilUtc,
    updatedAt,
  ];
  @override
  String get aliasedName => _alias ?? actualTableName;
  @override
  String get actualTableName => $name;
  static const String $name = 'registration_draft';
  @override
  VerificationContext validateIntegrity(
    Insertable<RegistrationDraft> instance, {
    bool isInserting = false,
  }) {
    final context = VerificationContext();
    final data = instance.toColumns(true);
    if (data.containsKey('id')) {
      context.handle(_idMeta, id.isAcceptableOrUnknown(data['id']!, _idMeta));
    }
    if (data.containsKey('step_index')) {
      context.handle(
        _stepIndexMeta,
        stepIndex.isAcceptableOrUnknown(data['step_index']!, _stepIndexMeta),
      );
    }
    if (data.containsKey('phone')) {
      context.handle(
        _phoneMeta,
        phone.isAcceptableOrUnknown(data['phone']!, _phoneMeta),
      );
    }
    if (data.containsKey('email')) {
      context.handle(
        _emailMeta,
        email.isAcceptableOrUnknown(data['email']!, _emailMeta),
      );
    }
    if (data.containsKey('state_code')) {
      context.handle(
        _stateCodeMeta,
        stateCode.isAcceptableOrUnknown(data['state_code']!, _stateCodeMeta),
      );
    }
    if (data.containsKey('lga_name')) {
      context.handle(
        _lgaNameMeta,
        lgaName.isAcceptableOrUnknown(data['lga_name']!, _lgaNameMeta),
      );
    }
    if (data.containsKey('pending_identity_id')) {
      context.handle(
        _pendingIdentityIdMeta,
        pendingIdentityId.isAcceptableOrUnknown(
          data['pending_identity_id']!,
          _pendingIdentityIdMeta,
        ),
      );
    }
    if (data.containsKey('farmer_id')) {
      context.handle(
        _farmerIdMeta,
        farmerId.isAcceptableOrUnknown(data['farmer_id']!, _farmerIdMeta),
      );
    }
    if (data.containsKey('status_message')) {
      context.handle(
        _statusMessageMeta,
        statusMessage.isAcceptableOrUnknown(
          data['status_message']!,
          _statusMessageMeta,
        ),
      );
    }
    if (data.containsKey('last_error_code')) {
      context.handle(
        _lastErrorCodeMeta,
        lastErrorCode.isAcceptableOrUnknown(
          data['last_error_code']!,
          _lastErrorCodeMeta,
        ),
      );
    }
    if (data.containsKey('lockout_until_utc')) {
      context.handle(
        _lockoutUntilUtcMeta,
        lockoutUntilUtc.isAcceptableOrUnknown(
          data['lockout_until_utc']!,
          _lockoutUntilUtcMeta,
        ),
      );
    }
    if (data.containsKey('updated_at')) {
      context.handle(
        _updatedAtMeta,
        updatedAt.isAcceptableOrUnknown(data['updated_at']!, _updatedAtMeta),
      );
    } else if (isInserting) {
      context.missing(_updatedAtMeta);
    }
    return context;
  }

  @override
  Set<GeneratedColumn> get $primaryKey => {id};
  @override
  RegistrationDraft map(Map<String, dynamic> data, {String? tablePrefix}) {
    final effectivePrefix = tablePrefix != null ? '$tablePrefix.' : '';
    return RegistrationDraft(
      id: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}id'],
      )!,
      stepIndex: attachedDatabase.typeMapping.read(
        DriftSqlType.int,
        data['${effectivePrefix}step_index'],
      )!,
      phone: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}phone'],
      )!,
      email: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}email'],
      )!,
      stateCode: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}state_code'],
      )!,
      lgaName: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}lga_name'],
      )!,
      pendingIdentityId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}pending_identity_id'],
      ),
      farmerId: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}farmer_id'],
      ),
      statusMessage: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}status_message'],
      ),
      lastErrorCode: attachedDatabase.typeMapping.read(
        DriftSqlType.string,
        data['${effectivePrefix}last_error_code'],
      ),
      lockoutUntilUtc: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}lockout_until_utc'],
      ),
      updatedAt: attachedDatabase.typeMapping.read(
        DriftSqlType.dateTime,
        data['${effectivePrefix}updated_at'],
      )!,
    );
  }

  @override
  $RegistrationDraftsTable createAlias(String alias) {
    return $RegistrationDraftsTable(attachedDatabase, alias);
  }
}

class RegistrationDraft extends DataClass
    implements Insertable<RegistrationDraft> {
  final int id;
  final int stepIndex;
  final String phone;
  final String email;
  final String stateCode;
  final String lgaName;
  final String? pendingIdentityId;
  final String? farmerId;
  final String? statusMessage;
  final String? lastErrorCode;
  final DateTime? lockoutUntilUtc;
  final DateTime updatedAt;
  const RegistrationDraft({
    required this.id,
    required this.stepIndex,
    required this.phone,
    required this.email,
    required this.stateCode,
    required this.lgaName,
    this.pendingIdentityId,
    this.farmerId,
    this.statusMessage,
    this.lastErrorCode,
    this.lockoutUntilUtc,
    required this.updatedAt,
  });
  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    map['id'] = Variable<int>(id);
    map['step_index'] = Variable<int>(stepIndex);
    map['phone'] = Variable<String>(phone);
    map['email'] = Variable<String>(email);
    map['state_code'] = Variable<String>(stateCode);
    map['lga_name'] = Variable<String>(lgaName);
    if (!nullToAbsent || pendingIdentityId != null) {
      map['pending_identity_id'] = Variable<String>(pendingIdentityId);
    }
    if (!nullToAbsent || farmerId != null) {
      map['farmer_id'] = Variable<String>(farmerId);
    }
    if (!nullToAbsent || statusMessage != null) {
      map['status_message'] = Variable<String>(statusMessage);
    }
    if (!nullToAbsent || lastErrorCode != null) {
      map['last_error_code'] = Variable<String>(lastErrorCode);
    }
    if (!nullToAbsent || lockoutUntilUtc != null) {
      map['lockout_until_utc'] = Variable<DateTime>(lockoutUntilUtc);
    }
    map['updated_at'] = Variable<DateTime>(updatedAt);
    return map;
  }

  RegistrationDraftsCompanion toCompanion(bool nullToAbsent) {
    return RegistrationDraftsCompanion(
      id: Value(id),
      stepIndex: Value(stepIndex),
      phone: Value(phone),
      email: Value(email),
      stateCode: Value(stateCode),
      lgaName: Value(lgaName),
      pendingIdentityId: pendingIdentityId == null && nullToAbsent
          ? const Value.absent()
          : Value(pendingIdentityId),
      farmerId: farmerId == null && nullToAbsent
          ? const Value.absent()
          : Value(farmerId),
      statusMessage: statusMessage == null && nullToAbsent
          ? const Value.absent()
          : Value(statusMessage),
      lastErrorCode: lastErrorCode == null && nullToAbsent
          ? const Value.absent()
          : Value(lastErrorCode),
      lockoutUntilUtc: lockoutUntilUtc == null && nullToAbsent
          ? const Value.absent()
          : Value(lockoutUntilUtc),
      updatedAt: Value(updatedAt),
    );
  }

  factory RegistrationDraft.fromJson(
    Map<String, dynamic> json, {
    ValueSerializer? serializer,
  }) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return RegistrationDraft(
      id: serializer.fromJson<int>(json['id']),
      stepIndex: serializer.fromJson<int>(json['stepIndex']),
      phone: serializer.fromJson<String>(json['phone']),
      email: serializer.fromJson<String>(json['email']),
      stateCode: serializer.fromJson<String>(json['stateCode']),
      lgaName: serializer.fromJson<String>(json['lgaName']),
      pendingIdentityId: serializer.fromJson<String?>(
        json['pendingIdentityId'],
      ),
      farmerId: serializer.fromJson<String?>(json['farmerId']),
      statusMessage: serializer.fromJson<String?>(json['statusMessage']),
      lastErrorCode: serializer.fromJson<String?>(json['lastErrorCode']),
      lockoutUntilUtc: serializer.fromJson<DateTime?>(json['lockoutUntilUtc']),
      updatedAt: serializer.fromJson<DateTime>(json['updatedAt']),
    );
  }
  @override
  Map<String, dynamic> toJson({ValueSerializer? serializer}) {
    serializer ??= driftRuntimeOptions.defaultSerializer;
    return <String, dynamic>{
      'id': serializer.toJson<int>(id),
      'stepIndex': serializer.toJson<int>(stepIndex),
      'phone': serializer.toJson<String>(phone),
      'email': serializer.toJson<String>(email),
      'stateCode': serializer.toJson<String>(stateCode),
      'lgaName': serializer.toJson<String>(lgaName),
      'pendingIdentityId': serializer.toJson<String?>(pendingIdentityId),
      'farmerId': serializer.toJson<String?>(farmerId),
      'statusMessage': serializer.toJson<String?>(statusMessage),
      'lastErrorCode': serializer.toJson<String?>(lastErrorCode),
      'lockoutUntilUtc': serializer.toJson<DateTime?>(lockoutUntilUtc),
      'updatedAt': serializer.toJson<DateTime>(updatedAt),
    };
  }

  RegistrationDraft copyWith({
    int? id,
    int? stepIndex,
    String? phone,
    String? email,
    String? stateCode,
    String? lgaName,
    Value<String?> pendingIdentityId = const Value.absent(),
    Value<String?> farmerId = const Value.absent(),
    Value<String?> statusMessage = const Value.absent(),
    Value<String?> lastErrorCode = const Value.absent(),
    Value<DateTime?> lockoutUntilUtc = const Value.absent(),
    DateTime? updatedAt,
  }) => RegistrationDraft(
    id: id ?? this.id,
    stepIndex: stepIndex ?? this.stepIndex,
    phone: phone ?? this.phone,
    email: email ?? this.email,
    stateCode: stateCode ?? this.stateCode,
    lgaName: lgaName ?? this.lgaName,
    pendingIdentityId: pendingIdentityId.present
        ? pendingIdentityId.value
        : this.pendingIdentityId,
    farmerId: farmerId.present ? farmerId.value : this.farmerId,
    statusMessage: statusMessage.present
        ? statusMessage.value
        : this.statusMessage,
    lastErrorCode: lastErrorCode.present
        ? lastErrorCode.value
        : this.lastErrorCode,
    lockoutUntilUtc: lockoutUntilUtc.present
        ? lockoutUntilUtc.value
        : this.lockoutUntilUtc,
    updatedAt: updatedAt ?? this.updatedAt,
  );
  RegistrationDraft copyWithCompanion(RegistrationDraftsCompanion data) {
    return RegistrationDraft(
      id: data.id.present ? data.id.value : this.id,
      stepIndex: data.stepIndex.present ? data.stepIndex.value : this.stepIndex,
      phone: data.phone.present ? data.phone.value : this.phone,
      email: data.email.present ? data.email.value : this.email,
      stateCode: data.stateCode.present ? data.stateCode.value : this.stateCode,
      lgaName: data.lgaName.present ? data.lgaName.value : this.lgaName,
      pendingIdentityId: data.pendingIdentityId.present
          ? data.pendingIdentityId.value
          : this.pendingIdentityId,
      farmerId: data.farmerId.present ? data.farmerId.value : this.farmerId,
      statusMessage: data.statusMessage.present
          ? data.statusMessage.value
          : this.statusMessage,
      lastErrorCode: data.lastErrorCode.present
          ? data.lastErrorCode.value
          : this.lastErrorCode,
      lockoutUntilUtc: data.lockoutUntilUtc.present
          ? data.lockoutUntilUtc.value
          : this.lockoutUntilUtc,
      updatedAt: data.updatedAt.present ? data.updatedAt.value : this.updatedAt,
    );
  }

  @override
  String toString() {
    return (StringBuffer('RegistrationDraft(')
          ..write('id: $id, ')
          ..write('stepIndex: $stepIndex, ')
          ..write('phone: $phone, ')
          ..write('email: $email, ')
          ..write('stateCode: $stateCode, ')
          ..write('lgaName: $lgaName, ')
          ..write('pendingIdentityId: $pendingIdentityId, ')
          ..write('farmerId: $farmerId, ')
          ..write('statusMessage: $statusMessage, ')
          ..write('lastErrorCode: $lastErrorCode, ')
          ..write('lockoutUntilUtc: $lockoutUntilUtc, ')
          ..write('updatedAt: $updatedAt')
          ..write(')'))
        .toString();
  }

  @override
  int get hashCode => Object.hash(
    id,
    stepIndex,
    phone,
    email,
    stateCode,
    lgaName,
    pendingIdentityId,
    farmerId,
    statusMessage,
    lastErrorCode,
    lockoutUntilUtc,
    updatedAt,
  );
  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      (other is RegistrationDraft &&
          other.id == this.id &&
          other.stepIndex == this.stepIndex &&
          other.phone == this.phone &&
          other.email == this.email &&
          other.stateCode == this.stateCode &&
          other.lgaName == this.lgaName &&
          other.pendingIdentityId == this.pendingIdentityId &&
          other.farmerId == this.farmerId &&
          other.statusMessage == this.statusMessage &&
          other.lastErrorCode == this.lastErrorCode &&
          other.lockoutUntilUtc == this.lockoutUntilUtc &&
          other.updatedAt == this.updatedAt);
}

class RegistrationDraftsCompanion extends UpdateCompanion<RegistrationDraft> {
  final Value<int> id;
  final Value<int> stepIndex;
  final Value<String> phone;
  final Value<String> email;
  final Value<String> stateCode;
  final Value<String> lgaName;
  final Value<String?> pendingIdentityId;
  final Value<String?> farmerId;
  final Value<String?> statusMessage;
  final Value<String?> lastErrorCode;
  final Value<DateTime?> lockoutUntilUtc;
  final Value<DateTime> updatedAt;
  const RegistrationDraftsCompanion({
    this.id = const Value.absent(),
    this.stepIndex = const Value.absent(),
    this.phone = const Value.absent(),
    this.email = const Value.absent(),
    this.stateCode = const Value.absent(),
    this.lgaName = const Value.absent(),
    this.pendingIdentityId = const Value.absent(),
    this.farmerId = const Value.absent(),
    this.statusMessage = const Value.absent(),
    this.lastErrorCode = const Value.absent(),
    this.lockoutUntilUtc = const Value.absent(),
    this.updatedAt = const Value.absent(),
  });
  RegistrationDraftsCompanion.insert({
    this.id = const Value.absent(),
    this.stepIndex = const Value.absent(),
    this.phone = const Value.absent(),
    this.email = const Value.absent(),
    this.stateCode = const Value.absent(),
    this.lgaName = const Value.absent(),
    this.pendingIdentityId = const Value.absent(),
    this.farmerId = const Value.absent(),
    this.statusMessage = const Value.absent(),
    this.lastErrorCode = const Value.absent(),
    this.lockoutUntilUtc = const Value.absent(),
    required DateTime updatedAt,
  }) : updatedAt = Value(updatedAt);
  static Insertable<RegistrationDraft> custom({
    Expression<int>? id,
    Expression<int>? stepIndex,
    Expression<String>? phone,
    Expression<String>? email,
    Expression<String>? stateCode,
    Expression<String>? lgaName,
    Expression<String>? pendingIdentityId,
    Expression<String>? farmerId,
    Expression<String>? statusMessage,
    Expression<String>? lastErrorCode,
    Expression<DateTime>? lockoutUntilUtc,
    Expression<DateTime>? updatedAt,
  }) {
    return RawValuesInsertable({
      if (id != null) 'id': id,
      if (stepIndex != null) 'step_index': stepIndex,
      if (phone != null) 'phone': phone,
      if (email != null) 'email': email,
      if (stateCode != null) 'state_code': stateCode,
      if (lgaName != null) 'lga_name': lgaName,
      if (pendingIdentityId != null) 'pending_identity_id': pendingIdentityId,
      if (farmerId != null) 'farmer_id': farmerId,
      if (statusMessage != null) 'status_message': statusMessage,
      if (lastErrorCode != null) 'last_error_code': lastErrorCode,
      if (lockoutUntilUtc != null) 'lockout_until_utc': lockoutUntilUtc,
      if (updatedAt != null) 'updated_at': updatedAt,
    });
  }

  RegistrationDraftsCompanion copyWith({
    Value<int>? id,
    Value<int>? stepIndex,
    Value<String>? phone,
    Value<String>? email,
    Value<String>? stateCode,
    Value<String>? lgaName,
    Value<String?>? pendingIdentityId,
    Value<String?>? farmerId,
    Value<String?>? statusMessage,
    Value<String?>? lastErrorCode,
    Value<DateTime?>? lockoutUntilUtc,
    Value<DateTime>? updatedAt,
  }) {
    return RegistrationDraftsCompanion(
      id: id ?? this.id,
      stepIndex: stepIndex ?? this.stepIndex,
      phone: phone ?? this.phone,
      email: email ?? this.email,
      stateCode: stateCode ?? this.stateCode,
      lgaName: lgaName ?? this.lgaName,
      pendingIdentityId: pendingIdentityId ?? this.pendingIdentityId,
      farmerId: farmerId ?? this.farmerId,
      statusMessage: statusMessage ?? this.statusMessage,
      lastErrorCode: lastErrorCode ?? this.lastErrorCode,
      lockoutUntilUtc: lockoutUntilUtc ?? this.lockoutUntilUtc,
      updatedAt: updatedAt ?? this.updatedAt,
    );
  }

  @override
  Map<String, Expression> toColumns(bool nullToAbsent) {
    final map = <String, Expression>{};
    if (id.present) {
      map['id'] = Variable<int>(id.value);
    }
    if (stepIndex.present) {
      map['step_index'] = Variable<int>(stepIndex.value);
    }
    if (phone.present) {
      map['phone'] = Variable<String>(phone.value);
    }
    if (email.present) {
      map['email'] = Variable<String>(email.value);
    }
    if (stateCode.present) {
      map['state_code'] = Variable<String>(stateCode.value);
    }
    if (lgaName.present) {
      map['lga_name'] = Variable<String>(lgaName.value);
    }
    if (pendingIdentityId.present) {
      map['pending_identity_id'] = Variable<String>(pendingIdentityId.value);
    }
    if (farmerId.present) {
      map['farmer_id'] = Variable<String>(farmerId.value);
    }
    if (statusMessage.present) {
      map['status_message'] = Variable<String>(statusMessage.value);
    }
    if (lastErrorCode.present) {
      map['last_error_code'] = Variable<String>(lastErrorCode.value);
    }
    if (lockoutUntilUtc.present) {
      map['lockout_until_utc'] = Variable<DateTime>(lockoutUntilUtc.value);
    }
    if (updatedAt.present) {
      map['updated_at'] = Variable<DateTime>(updatedAt.value);
    }
    return map;
  }

  @override
  String toString() {
    return (StringBuffer('RegistrationDraftsCompanion(')
          ..write('id: $id, ')
          ..write('stepIndex: $stepIndex, ')
          ..write('phone: $phone, ')
          ..write('email: $email, ')
          ..write('stateCode: $stateCode, ')
          ..write('lgaName: $lgaName, ')
          ..write('pendingIdentityId: $pendingIdentityId, ')
          ..write('farmerId: $farmerId, ')
          ..write('statusMessage: $statusMessage, ')
          ..write('lastErrorCode: $lastErrorCode, ')
          ..write('lockoutUntilUtc: $lockoutUntilUtc, ')
          ..write('updatedAt: $updatedAt')
          ..write(')'))
        .toString();
  }
}

abstract class _$AppOutboxDatabase extends GeneratedDatabase {
  _$AppOutboxDatabase(QueryExecutor e) : super(e);
  $AppOutboxDatabaseManager get managers => $AppOutboxDatabaseManager(this);
  late final $LocalOutboxMutationsTable localOutboxMutations =
      $LocalOutboxMutationsTable(this);
  late final $RegistrationDraftsTable registrationDrafts =
      $RegistrationDraftsTable(this);
  @override
  Iterable<TableInfo<Table, Object?>> get allTables =>
      allSchemaEntities.whereType<TableInfo<Table, Object?>>();
  @override
  List<DatabaseSchemaEntity> get allSchemaEntities => [
    localOutboxMutations,
    registrationDrafts,
  ];
}

typedef $$LocalOutboxMutationsTableCreateCompanionBuilder =
    LocalOutboxMutationsCompanion Function({
      Value<int> seqId,
      required String clientGuid,
      required String entityType,
      required String entityLocalId,
      Value<String?> parentEntityLocalId,
      required String httpMethod,
      required String endpointPath,
      required String payloadJson,
      Value<String?> attachmentLocalPaths,
      required DateTime clientRecordedAt,
      required int monotonicElapsedMs,
      Value<String?> actorIdentityId,
      Value<String?> agentSessionId,
      Value<String?> fobIdUsed,
      Value<DateTime?> fobTappedAt,
      Value<String> syncStatus,
      Value<int> retryCount,
      Value<DateTime?> nextRetryAt,
      Value<String?> lastErrorCode,
      Value<DateTime?> serverReceivedAt,
    });
typedef $$LocalOutboxMutationsTableUpdateCompanionBuilder =
    LocalOutboxMutationsCompanion Function({
      Value<int> seqId,
      Value<String> clientGuid,
      Value<String> entityType,
      Value<String> entityLocalId,
      Value<String?> parentEntityLocalId,
      Value<String> httpMethod,
      Value<String> endpointPath,
      Value<String> payloadJson,
      Value<String?> attachmentLocalPaths,
      Value<DateTime> clientRecordedAt,
      Value<int> monotonicElapsedMs,
      Value<String?> actorIdentityId,
      Value<String?> agentSessionId,
      Value<String?> fobIdUsed,
      Value<DateTime?> fobTappedAt,
      Value<String> syncStatus,
      Value<int> retryCount,
      Value<DateTime?> nextRetryAt,
      Value<String?> lastErrorCode,
      Value<DateTime?> serverReceivedAt,
    });

class $$LocalOutboxMutationsTableFilterComposer
    extends Composer<_$AppOutboxDatabase, $LocalOutboxMutationsTable> {
  $$LocalOutboxMutationsTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<int> get seqId => $composableBuilder(
    column: $table.seqId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get clientGuid => $composableBuilder(
    column: $table.clientGuid,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get entityType => $composableBuilder(
    column: $table.entityType,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get entityLocalId => $composableBuilder(
    column: $table.entityLocalId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get parentEntityLocalId => $composableBuilder(
    column: $table.parentEntityLocalId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get httpMethod => $composableBuilder(
    column: $table.httpMethod,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get endpointPath => $composableBuilder(
    column: $table.endpointPath,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get payloadJson => $composableBuilder(
    column: $table.payloadJson,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get attachmentLocalPaths => $composableBuilder(
    column: $table.attachmentLocalPaths,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get clientRecordedAt => $composableBuilder(
    column: $table.clientRecordedAt,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<int> get monotonicElapsedMs => $composableBuilder(
    column: $table.monotonicElapsedMs,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get actorIdentityId => $composableBuilder(
    column: $table.actorIdentityId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get agentSessionId => $composableBuilder(
    column: $table.agentSessionId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get fobIdUsed => $composableBuilder(
    column: $table.fobIdUsed,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get fobTappedAt => $composableBuilder(
    column: $table.fobTappedAt,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get syncStatus => $composableBuilder(
    column: $table.syncStatus,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<int> get retryCount => $composableBuilder(
    column: $table.retryCount,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get nextRetryAt => $composableBuilder(
    column: $table.nextRetryAt,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get lastErrorCode => $composableBuilder(
    column: $table.lastErrorCode,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get serverReceivedAt => $composableBuilder(
    column: $table.serverReceivedAt,
    builder: (column) => ColumnFilters(column),
  );
}

class $$LocalOutboxMutationsTableOrderingComposer
    extends Composer<_$AppOutboxDatabase, $LocalOutboxMutationsTable> {
  $$LocalOutboxMutationsTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<int> get seqId => $composableBuilder(
    column: $table.seqId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get clientGuid => $composableBuilder(
    column: $table.clientGuid,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get entityType => $composableBuilder(
    column: $table.entityType,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get entityLocalId => $composableBuilder(
    column: $table.entityLocalId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get parentEntityLocalId => $composableBuilder(
    column: $table.parentEntityLocalId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get httpMethod => $composableBuilder(
    column: $table.httpMethod,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get endpointPath => $composableBuilder(
    column: $table.endpointPath,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get payloadJson => $composableBuilder(
    column: $table.payloadJson,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get attachmentLocalPaths => $composableBuilder(
    column: $table.attachmentLocalPaths,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get clientRecordedAt => $composableBuilder(
    column: $table.clientRecordedAt,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<int> get monotonicElapsedMs => $composableBuilder(
    column: $table.monotonicElapsedMs,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get actorIdentityId => $composableBuilder(
    column: $table.actorIdentityId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get agentSessionId => $composableBuilder(
    column: $table.agentSessionId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get fobIdUsed => $composableBuilder(
    column: $table.fobIdUsed,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get fobTappedAt => $composableBuilder(
    column: $table.fobTappedAt,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get syncStatus => $composableBuilder(
    column: $table.syncStatus,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<int> get retryCount => $composableBuilder(
    column: $table.retryCount,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get nextRetryAt => $composableBuilder(
    column: $table.nextRetryAt,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get lastErrorCode => $composableBuilder(
    column: $table.lastErrorCode,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get serverReceivedAt => $composableBuilder(
    column: $table.serverReceivedAt,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$LocalOutboxMutationsTableAnnotationComposer
    extends Composer<_$AppOutboxDatabase, $LocalOutboxMutationsTable> {
  $$LocalOutboxMutationsTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<int> get seqId =>
      $composableBuilder(column: $table.seqId, builder: (column) => column);

  GeneratedColumn<String> get clientGuid => $composableBuilder(
    column: $table.clientGuid,
    builder: (column) => column,
  );

  GeneratedColumn<String> get entityType => $composableBuilder(
    column: $table.entityType,
    builder: (column) => column,
  );

  GeneratedColumn<String> get entityLocalId => $composableBuilder(
    column: $table.entityLocalId,
    builder: (column) => column,
  );

  GeneratedColumn<String> get parentEntityLocalId => $composableBuilder(
    column: $table.parentEntityLocalId,
    builder: (column) => column,
  );

  GeneratedColumn<String> get httpMethod => $composableBuilder(
    column: $table.httpMethod,
    builder: (column) => column,
  );

  GeneratedColumn<String> get endpointPath => $composableBuilder(
    column: $table.endpointPath,
    builder: (column) => column,
  );

  GeneratedColumn<String> get payloadJson => $composableBuilder(
    column: $table.payloadJson,
    builder: (column) => column,
  );

  GeneratedColumn<String> get attachmentLocalPaths => $composableBuilder(
    column: $table.attachmentLocalPaths,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get clientRecordedAt => $composableBuilder(
    column: $table.clientRecordedAt,
    builder: (column) => column,
  );

  GeneratedColumn<int> get monotonicElapsedMs => $composableBuilder(
    column: $table.monotonicElapsedMs,
    builder: (column) => column,
  );

  GeneratedColumn<String> get actorIdentityId => $composableBuilder(
    column: $table.actorIdentityId,
    builder: (column) => column,
  );

  GeneratedColumn<String> get agentSessionId => $composableBuilder(
    column: $table.agentSessionId,
    builder: (column) => column,
  );

  GeneratedColumn<String> get fobIdUsed =>
      $composableBuilder(column: $table.fobIdUsed, builder: (column) => column);

  GeneratedColumn<DateTime> get fobTappedAt => $composableBuilder(
    column: $table.fobTappedAt,
    builder: (column) => column,
  );

  GeneratedColumn<String> get syncStatus => $composableBuilder(
    column: $table.syncStatus,
    builder: (column) => column,
  );

  GeneratedColumn<int> get retryCount => $composableBuilder(
    column: $table.retryCount,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get nextRetryAt => $composableBuilder(
    column: $table.nextRetryAt,
    builder: (column) => column,
  );

  GeneratedColumn<String> get lastErrorCode => $composableBuilder(
    column: $table.lastErrorCode,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get serverReceivedAt => $composableBuilder(
    column: $table.serverReceivedAt,
    builder: (column) => column,
  );
}

class $$LocalOutboxMutationsTableTableManager
    extends
        RootTableManager<
          _$AppOutboxDatabase,
          $LocalOutboxMutationsTable,
          LocalOutboxMutation,
          $$LocalOutboxMutationsTableFilterComposer,
          $$LocalOutboxMutationsTableOrderingComposer,
          $$LocalOutboxMutationsTableAnnotationComposer,
          $$LocalOutboxMutationsTableCreateCompanionBuilder,
          $$LocalOutboxMutationsTableUpdateCompanionBuilder,
          (
            LocalOutboxMutation,
            BaseReferences<
              _$AppOutboxDatabase,
              $LocalOutboxMutationsTable,
              LocalOutboxMutation
            >,
          ),
          LocalOutboxMutation,
          PrefetchHooks Function()
        > {
  $$LocalOutboxMutationsTableTableManager(
    _$AppOutboxDatabase db,
    $LocalOutboxMutationsTable table,
  ) : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$LocalOutboxMutationsTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$LocalOutboxMutationsTableOrderingComposer(
                $db: db,
                $table: table,
              ),
          createComputedFieldComposer: () =>
              $$LocalOutboxMutationsTableAnnotationComposer(
                $db: db,
                $table: table,
              ),
          updateCompanionCallback:
              ({
                Value<int> seqId = const Value.absent(),
                Value<String> clientGuid = const Value.absent(),
                Value<String> entityType = const Value.absent(),
                Value<String> entityLocalId = const Value.absent(),
                Value<String?> parentEntityLocalId = const Value.absent(),
                Value<String> httpMethod = const Value.absent(),
                Value<String> endpointPath = const Value.absent(),
                Value<String> payloadJson = const Value.absent(),
                Value<String?> attachmentLocalPaths = const Value.absent(),
                Value<DateTime> clientRecordedAt = const Value.absent(),
                Value<int> monotonicElapsedMs = const Value.absent(),
                Value<String?> actorIdentityId = const Value.absent(),
                Value<String?> agentSessionId = const Value.absent(),
                Value<String?> fobIdUsed = const Value.absent(),
                Value<DateTime?> fobTappedAt = const Value.absent(),
                Value<String> syncStatus = const Value.absent(),
                Value<int> retryCount = const Value.absent(),
                Value<DateTime?> nextRetryAt = const Value.absent(),
                Value<String?> lastErrorCode = const Value.absent(),
                Value<DateTime?> serverReceivedAt = const Value.absent(),
              }) => LocalOutboxMutationsCompanion(
                seqId: seqId,
                clientGuid: clientGuid,
                entityType: entityType,
                entityLocalId: entityLocalId,
                parentEntityLocalId: parentEntityLocalId,
                httpMethod: httpMethod,
                endpointPath: endpointPath,
                payloadJson: payloadJson,
                attachmentLocalPaths: attachmentLocalPaths,
                clientRecordedAt: clientRecordedAt,
                monotonicElapsedMs: monotonicElapsedMs,
                actorIdentityId: actorIdentityId,
                agentSessionId: agentSessionId,
                fobIdUsed: fobIdUsed,
                fobTappedAt: fobTappedAt,
                syncStatus: syncStatus,
                retryCount: retryCount,
                nextRetryAt: nextRetryAt,
                lastErrorCode: lastErrorCode,
                serverReceivedAt: serverReceivedAt,
              ),
          createCompanionCallback:
              ({
                Value<int> seqId = const Value.absent(),
                required String clientGuid,
                required String entityType,
                required String entityLocalId,
                Value<String?> parentEntityLocalId = const Value.absent(),
                required String httpMethod,
                required String endpointPath,
                required String payloadJson,
                Value<String?> attachmentLocalPaths = const Value.absent(),
                required DateTime clientRecordedAt,
                required int monotonicElapsedMs,
                Value<String?> actorIdentityId = const Value.absent(),
                Value<String?> agentSessionId = const Value.absent(),
                Value<String?> fobIdUsed = const Value.absent(),
                Value<DateTime?> fobTappedAt = const Value.absent(),
                Value<String> syncStatus = const Value.absent(),
                Value<int> retryCount = const Value.absent(),
                Value<DateTime?> nextRetryAt = const Value.absent(),
                Value<String?> lastErrorCode = const Value.absent(),
                Value<DateTime?> serverReceivedAt = const Value.absent(),
              }) => LocalOutboxMutationsCompanion.insert(
                seqId: seqId,
                clientGuid: clientGuid,
                entityType: entityType,
                entityLocalId: entityLocalId,
                parentEntityLocalId: parentEntityLocalId,
                httpMethod: httpMethod,
                endpointPath: endpointPath,
                payloadJson: payloadJson,
                attachmentLocalPaths: attachmentLocalPaths,
                clientRecordedAt: clientRecordedAt,
                monotonicElapsedMs: monotonicElapsedMs,
                actorIdentityId: actorIdentityId,
                agentSessionId: agentSessionId,
                fobIdUsed: fobIdUsed,
                fobTappedAt: fobTappedAt,
                syncStatus: syncStatus,
                retryCount: retryCount,
                nextRetryAt: nextRetryAt,
                lastErrorCode: lastErrorCode,
                serverReceivedAt: serverReceivedAt,
              ),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$LocalOutboxMutationsTable, LocalOutboxMutation>(
                    table,
                  ),
                  BaseReferences<
                    _$AppOutboxDatabase,
                    $LocalOutboxMutationsTable,
                    LocalOutboxMutation
                  >(db, table, e),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$LocalOutboxMutationsTableProcessedTableManager =
    ProcessedTableManager<
      _$AppOutboxDatabase,
      $LocalOutboxMutationsTable,
      LocalOutboxMutation,
      $$LocalOutboxMutationsTableFilterComposer,
      $$LocalOutboxMutationsTableOrderingComposer,
      $$LocalOutboxMutationsTableAnnotationComposer,
      $$LocalOutboxMutationsTableCreateCompanionBuilder,
      $$LocalOutboxMutationsTableUpdateCompanionBuilder,
      (
        LocalOutboxMutation,
        BaseReferences<
          _$AppOutboxDatabase,
          $LocalOutboxMutationsTable,
          LocalOutboxMutation
        >,
      ),
      LocalOutboxMutation,
      PrefetchHooks Function()
    >;
typedef $$RegistrationDraftsTableCreateCompanionBuilder =
    RegistrationDraftsCompanion Function({
      Value<int> id,
      Value<int> stepIndex,
      Value<String> phone,
      Value<String> email,
      Value<String> stateCode,
      Value<String> lgaName,
      Value<String?> pendingIdentityId,
      Value<String?> farmerId,
      Value<String?> statusMessage,
      Value<String?> lastErrorCode,
      Value<DateTime?> lockoutUntilUtc,
      required DateTime updatedAt,
    });
typedef $$RegistrationDraftsTableUpdateCompanionBuilder =
    RegistrationDraftsCompanion Function({
      Value<int> id,
      Value<int> stepIndex,
      Value<String> phone,
      Value<String> email,
      Value<String> stateCode,
      Value<String> lgaName,
      Value<String?> pendingIdentityId,
      Value<String?> farmerId,
      Value<String?> statusMessage,
      Value<String?> lastErrorCode,
      Value<DateTime?> lockoutUntilUtc,
      Value<DateTime> updatedAt,
    });

class $$RegistrationDraftsTableFilterComposer
    extends Composer<_$AppOutboxDatabase, $RegistrationDraftsTable> {
  $$RegistrationDraftsTableFilterComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnFilters<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<int> get stepIndex => $composableBuilder(
    column: $table.stepIndex,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get phone => $composableBuilder(
    column: $table.phone,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get email => $composableBuilder(
    column: $table.email,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get stateCode => $composableBuilder(
    column: $table.stateCode,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get lgaName => $composableBuilder(
    column: $table.lgaName,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get pendingIdentityId => $composableBuilder(
    column: $table.pendingIdentityId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get farmerId => $composableBuilder(
    column: $table.farmerId,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get statusMessage => $composableBuilder(
    column: $table.statusMessage,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<String> get lastErrorCode => $composableBuilder(
    column: $table.lastErrorCode,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get lockoutUntilUtc => $composableBuilder(
    column: $table.lockoutUntilUtc,
    builder: (column) => ColumnFilters(column),
  );

  ColumnFilters<DateTime> get updatedAt => $composableBuilder(
    column: $table.updatedAt,
    builder: (column) => ColumnFilters(column),
  );
}

class $$RegistrationDraftsTableOrderingComposer
    extends Composer<_$AppOutboxDatabase, $RegistrationDraftsTable> {
  $$RegistrationDraftsTableOrderingComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  ColumnOrderings<int> get id => $composableBuilder(
    column: $table.id,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<int> get stepIndex => $composableBuilder(
    column: $table.stepIndex,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get phone => $composableBuilder(
    column: $table.phone,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get email => $composableBuilder(
    column: $table.email,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get stateCode => $composableBuilder(
    column: $table.stateCode,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get lgaName => $composableBuilder(
    column: $table.lgaName,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get pendingIdentityId => $composableBuilder(
    column: $table.pendingIdentityId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get farmerId => $composableBuilder(
    column: $table.farmerId,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get statusMessage => $composableBuilder(
    column: $table.statusMessage,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<String> get lastErrorCode => $composableBuilder(
    column: $table.lastErrorCode,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get lockoutUntilUtc => $composableBuilder(
    column: $table.lockoutUntilUtc,
    builder: (column) => ColumnOrderings(column),
  );

  ColumnOrderings<DateTime> get updatedAt => $composableBuilder(
    column: $table.updatedAt,
    builder: (column) => ColumnOrderings(column),
  );
}

class $$RegistrationDraftsTableAnnotationComposer
    extends Composer<_$AppOutboxDatabase, $RegistrationDraftsTable> {
  $$RegistrationDraftsTableAnnotationComposer({
    required super.$db,
    required super.$table,
    super.joinBuilder,
    super.$addJoinBuilderToRootComposer,
    super.$removeJoinBuilderFromRootComposer,
  });
  GeneratedColumn<int> get id =>
      $composableBuilder(column: $table.id, builder: (column) => column);

  GeneratedColumn<int> get stepIndex =>
      $composableBuilder(column: $table.stepIndex, builder: (column) => column);

  GeneratedColumn<String> get phone =>
      $composableBuilder(column: $table.phone, builder: (column) => column);

  GeneratedColumn<String> get email =>
      $composableBuilder(column: $table.email, builder: (column) => column);

  GeneratedColumn<String> get stateCode =>
      $composableBuilder(column: $table.stateCode, builder: (column) => column);

  GeneratedColumn<String> get lgaName =>
      $composableBuilder(column: $table.lgaName, builder: (column) => column);

  GeneratedColumn<String> get pendingIdentityId => $composableBuilder(
    column: $table.pendingIdentityId,
    builder: (column) => column,
  );

  GeneratedColumn<String> get farmerId =>
      $composableBuilder(column: $table.farmerId, builder: (column) => column);

  GeneratedColumn<String> get statusMessage => $composableBuilder(
    column: $table.statusMessage,
    builder: (column) => column,
  );

  GeneratedColumn<String> get lastErrorCode => $composableBuilder(
    column: $table.lastErrorCode,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get lockoutUntilUtc => $composableBuilder(
    column: $table.lockoutUntilUtc,
    builder: (column) => column,
  );

  GeneratedColumn<DateTime> get updatedAt =>
      $composableBuilder(column: $table.updatedAt, builder: (column) => column);
}

class $$RegistrationDraftsTableTableManager
    extends
        RootTableManager<
          _$AppOutboxDatabase,
          $RegistrationDraftsTable,
          RegistrationDraft,
          $$RegistrationDraftsTableFilterComposer,
          $$RegistrationDraftsTableOrderingComposer,
          $$RegistrationDraftsTableAnnotationComposer,
          $$RegistrationDraftsTableCreateCompanionBuilder,
          $$RegistrationDraftsTableUpdateCompanionBuilder,
          (
            RegistrationDraft,
            BaseReferences<
              _$AppOutboxDatabase,
              $RegistrationDraftsTable,
              RegistrationDraft
            >,
          ),
          RegistrationDraft,
          PrefetchHooks Function()
        > {
  $$RegistrationDraftsTableTableManager(
    _$AppOutboxDatabase db,
    $RegistrationDraftsTable table,
  ) : super(
        TableManagerState(
          db: db,
          table: table,
          createFilteringComposer: () =>
              $$RegistrationDraftsTableFilterComposer($db: db, $table: table),
          createOrderingComposer: () =>
              $$RegistrationDraftsTableOrderingComposer($db: db, $table: table),
          createComputedFieldComposer: () =>
              $$RegistrationDraftsTableAnnotationComposer(
                $db: db,
                $table: table,
              ),
          updateCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                Value<int> stepIndex = const Value.absent(),
                Value<String> phone = const Value.absent(),
                Value<String> email = const Value.absent(),
                Value<String> stateCode = const Value.absent(),
                Value<String> lgaName = const Value.absent(),
                Value<String?> pendingIdentityId = const Value.absent(),
                Value<String?> farmerId = const Value.absent(),
                Value<String?> statusMessage = const Value.absent(),
                Value<String?> lastErrorCode = const Value.absent(),
                Value<DateTime?> lockoutUntilUtc = const Value.absent(),
                Value<DateTime> updatedAt = const Value.absent(),
              }) => RegistrationDraftsCompanion(
                id: id,
                stepIndex: stepIndex,
                phone: phone,
                email: email,
                stateCode: stateCode,
                lgaName: lgaName,
                pendingIdentityId: pendingIdentityId,
                farmerId: farmerId,
                statusMessage: statusMessage,
                lastErrorCode: lastErrorCode,
                lockoutUntilUtc: lockoutUntilUtc,
                updatedAt: updatedAt,
              ),
          createCompanionCallback:
              ({
                Value<int> id = const Value.absent(),
                Value<int> stepIndex = const Value.absent(),
                Value<String> phone = const Value.absent(),
                Value<String> email = const Value.absent(),
                Value<String> stateCode = const Value.absent(),
                Value<String> lgaName = const Value.absent(),
                Value<String?> pendingIdentityId = const Value.absent(),
                Value<String?> farmerId = const Value.absent(),
                Value<String?> statusMessage = const Value.absent(),
                Value<String?> lastErrorCode = const Value.absent(),
                Value<DateTime?> lockoutUntilUtc = const Value.absent(),
                required DateTime updatedAt,
              }) => RegistrationDraftsCompanion.insert(
                id: id,
                stepIndex: stepIndex,
                phone: phone,
                email: email,
                stateCode: stateCode,
                lgaName: lgaName,
                pendingIdentityId: pendingIdentityId,
                farmerId: farmerId,
                statusMessage: statusMessage,
                lastErrorCode: lastErrorCode,
                lockoutUntilUtc: lockoutUntilUtc,
                updatedAt: updatedAt,
              ),
          withReferenceMapper: (p0) => p0
              .map(
                (e) => (
                  e.readTable<$RegistrationDraftsTable, RegistrationDraft>(
                    table,
                  ),
                  BaseReferences<
                    _$AppOutboxDatabase,
                    $RegistrationDraftsTable,
                    RegistrationDraft
                  >(db, table, e),
                ),
              )
              .toList(),
          prefetchHooksCallback: null,
        ),
      );
}

typedef $$RegistrationDraftsTableProcessedTableManager =
    ProcessedTableManager<
      _$AppOutboxDatabase,
      $RegistrationDraftsTable,
      RegistrationDraft,
      $$RegistrationDraftsTableFilterComposer,
      $$RegistrationDraftsTableOrderingComposer,
      $$RegistrationDraftsTableAnnotationComposer,
      $$RegistrationDraftsTableCreateCompanionBuilder,
      $$RegistrationDraftsTableUpdateCompanionBuilder,
      (
        RegistrationDraft,
        BaseReferences<
          _$AppOutboxDatabase,
          $RegistrationDraftsTable,
          RegistrationDraft
        >,
      ),
      RegistrationDraft,
      PrefetchHooks Function()
    >;

class $AppOutboxDatabaseManager {
  final _$AppOutboxDatabase _db;
  $AppOutboxDatabaseManager(this._db);
  $$LocalOutboxMutationsTableTableManager get localOutboxMutations =>
      $$LocalOutboxMutationsTableTableManager(_db, _db.localOutboxMutations);
  $$RegistrationDraftsTableTableManager get registrationDrafts =>
      $$RegistrationDraftsTableTableManager(_db, _db.registrationDrafts);
}
