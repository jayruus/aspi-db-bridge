.PHONY: help up down restart logs build clean shell status

help: ## Mostra questo messaggio di aiuto
	@echo "Comandi disponibili per DbBridge ASP.NET:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}'

build: ## Costruisci l'immagine Docker
	@echo "🔨 Building DbBridge image..."
	docker compose build
	@echo "✅ Build completato!"

up: ## Avvia il container DbBridge
	@echo "🚀 Avvio DbBridge..."
	docker compose up -d
	@echo "✅ DbBridge avviato!"
	@echo "🌐 Bridge disponibile su: http://localhost:5001"
	@echo "📋 Health check: curl http://localhost:5001/health"

down: ## Ferma il container
	@echo "🛑 Arresto DbBridge..."
	docker compose down

restart: ## Riavvia il container
	@make down
	@make up

logs: ## Mostra i log del container
	docker compose logs -f

shell: ## Accedi alla shell del container
	docker exec -it aspi-dbbridge bash

clean: ## Rimuovi container e immagine
	@echo "🧹 Pulizia completa..."
	docker compose down --rmi all
	@echo "✅ Pulizia completata"

status: ## Mostra lo stato del container
	@docker compose ps