terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.11"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.7"
    }
  }
}

provider "azurerm" {
  subscription_id = var.subscription_id

  # azurerm 5.x no longer registers resource providers by default.
  # A fresh Visual Studio subscription needs Compute, Network and
  # CognitiveServices registered, so keep the 4.x behaviour.
  resource_provider_registrations = "legacy"

  features {}
}
