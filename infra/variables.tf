variable "subscription_id" {
  description = "Azure subscription ID (the Visual Studio credit subscription)."
  type        = string
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "switzerlandnorth"
}

variable "name" {
  description = "Base name used for resource names."
  type        = string
  default     = "shelfie"
}

variable "vm_size" {
  description = "VM size. 2 GB RAM is plenty for Part-DB with SQLite and the capture API."
  type        = string
  default     = "Standard_B1ms"
}

variable "admin_username" {
  description = "Linux admin user on the VM."
  type        = string
  default     = "roman"
}

variable "admin_ssh_public_key" {
  description = "SSH public key for the admin user (fallback access; day-to-day access uses Tailscale SSH)."
  type        = string
}

variable "tailscale_auth_key" {
  description = "One-time, pre-approved Tailscale auth key for the VM host. Create it in the Tailscale admin console."
  type        = string
  sensitive   = true
}

variable "tailscale_hostname" {
  description = "Hostname of the VM on the tailnet."
  type        = string
  default     = "shelfie-vm"
}

variable "openai_model_name" {
  description = "Azure OpenAI model used for part identification. Must support image input and structured outputs."
  type        = string
  default     = "gpt-5-mini"
}

variable "openai_model_version" {
  description = "Model version. Check the Azure model retirement schedule before changing."
  type        = string
  default     = "2025-08-07"
}

variable "openai_deployment_sku" {
  description = "Deployment type. gpt-5-mini is only offered as GlobalStandard in Switzerland North."
  type        = string
  default     = "GlobalStandard"
}

variable "openai_capacity" {
  description = "Deployment capacity in thousands of tokens per minute."
  type        = number
  default     = 20
}

variable "tags" {
  description = "Tags applied to all resources."
  type        = map(string)
  default = {
    project = "shelfie"
    purpose = "dev-test"
  }
}
