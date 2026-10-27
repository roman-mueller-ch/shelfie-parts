output "vm_tailscale_hostname" {
  description = "Reach the VM with: ssh <admin_username>@<this name> (Tailscale SSH)."
  value       = var.tailscale_hostname
}

output "vm_public_ip" {
  description = "Outbound IP only. Nothing listens on it."
  value       = azurerm_public_ip.vm.ip_address
}

output "openai_base_url" {
  description = "OpenAI-compatible base URL (Azure OpenAI v1 API). Use with any OpenAI SDK."
  value       = "${trimsuffix(azurerm_cognitive_account.openai.endpoint, "/")}/openai/v1/"
}

output "openai_model" {
  description = "Deployment name to pass as the model."
  value       = azurerm_cognitive_deployment.vision.name
}

output "openai_api_key" {
  description = "Read with: terraform output -raw openai_api_key"
  value       = azurerm_cognitive_account.openai.primary_access_key
  sensitive   = true
}
