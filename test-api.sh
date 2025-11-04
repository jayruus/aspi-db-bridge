#!/bin/bash

# Script per testare il DbBridge ASP: "login" (get user), recupero eventi per tecnico 87 e filtro

BRIDGE_BASE="http://127.0.0.1:5001"
SECRET="dev-bridge-secret-2025"

echo "=== 'Login' - Recupero utente 'fast' ==="
USER_RESPONSE=$(curl -s -X POST "$BRIDGE_BASE/db/exec" \
  -H "Content-Type: application/json" \
  -H "X-Bridge-Secret: $SECRET" \
  -d '{"QueryKey": "User_GetByUsername_8f4a2c", "Params": {"p1": "fast"}}')

echo "Risposta user: $USER_RESPONSE"

echo ""
echo "=== Recupero eventi per tecnico 87 ==="
EVENTI_RESPONSE=$(curl -s -X POST "$BRIDGE_BASE/db/exec" \
  -H "Content-Type: application/json" \
  -H "X-Bridge-Secret: $SECRET" \
  -d '{"QueryKey": "Evento_GetEventiByTecnico_6e1f9d", "Params": {"p1": "87", "p2": 0, "p3": 10}}')

echo "Risposta eventi: $EVENTI_RESPONSE"

echo ""
echo "=== Recupero eventi ASSEGNATI per tecnico 87 ==="
ASSEGNATI_RESPONSE=$(curl -s -X POST "$BRIDGE_BASE/db/exec" \
  -H "Content-Type: application/json" \
  -H "X-Bridge-Secret: $SECRET" \
  -d '{"QueryKey": "Evento_GetAssegnati_9e7f3a", "Params": {"p1": "87", "p2": 0, "p3": 20}}')

echo "Risposta assegnati: $ASSEGNATI_RESPONSE"

echo ""
echo "=== Recupero eventi PIANIFICATI per tecnico 87 ==="
PIANIFICATI_RESPONSE=$(curl -s -X POST "$BRIDGE_BASE/db/exec" \
  -H "Content-Type: application/json" \
  -H "X-Bridge-Secret: $SECRET" \
  -d '{"QueryKey": "Evento_GetPianificati_5c8b2f", "Params": {"p1": "87", "p2": 0, "p3": 20}}')

echo "Risposta pianificati: $PIANIFICATI_RESPONSE"

echo ""
echo "=== Filtro eventi (esempio: solo quelli con stato 'aperto', assumendo campo 'stato') ==="
# Filtro semplice con jq (se disponibile)
if command -v jq &> /dev/null; then
  EVENTI_FILTRATI=$(echo "$EVENTI_RESPONSE" | jq '.rows | map(select(.stato == "aperto"))')
  echo "Eventi filtrati (stato=aperto): $EVENTI_FILTRATI"
else
  echo "jq non disponibile, impossibile filtrare. Installa jq per filtri avanzati."
  echo "Eventi grezzi: $EVENTI_RESPONSE"
fi

echo ""
echo "=== Test completato ==="