using Microsoft.EntityFrameworkCore;

namespace WaLeads.Infrastructure;

public sealed class LeadDbContext(DbContextOptions<LeadDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(tenant => tenant.Id);
            entity.Property(tenant => tenant.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(tenant => tenant.Name).HasColumnName("name").IsRequired();
            entity.Property(tenant => tenant.PhoneNumberId).HasColumnName("phone_number_id").IsRequired();
            entity.HasIndex(tenant => tenant.PhoneNumberId).IsUnique();
            entity.Property(tenant => tenant.BusinessContext).HasColumnName("business_context").IsRequired();
            entity.Property(tenant => tenant.Active).HasColumnName("active").HasDefaultValue(true);
            entity.Property(tenant => tenant.HotThreshold).HasColumnName("hot_threshold").HasDefaultValue(70);
            entity.Property(tenant => tenant.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("leads");
            entity.HasKey(lead => lead.Id);
            entity.Property(lead => lead.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(lead => lead.TenantId).HasColumnName("tenant_id");
            entity.Property(lead => lead.WaId).HasColumnName("wa_id").IsRequired();
            entity.Property(lead => lead.ProfileName).HasColumnName("profile_name");
            entity.Property(lead => lead.Score).HasColumnName("score");
            entity.Property(lead => lead.Intent).HasColumnName("intent");
            entity.Property(lead => lead.Stage).HasColumnName("stage").HasDefaultValue("new");
            entity.Property(lead => lead.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(lead => lead.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            entity.Property(lead => lead.QualifyClaimedUntil).HasColumnName("qualify_claimed_until");
            entity.Property(lead => lead.QualifyAttempts).HasColumnName("qualify_attempts").HasDefaultValue(0);
            entity.HasIndex(lead => new { lead.TenantId, lead.WaId }).IsUnique();
            entity.HasOne(lead => lead.Tenant).WithMany(tenant => tenant.Leads)
                .HasForeignKey(lead => lead.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(message => message.LeadId).HasColumnName("lead_id");
            entity.Property(message => message.WaMessageId).HasColumnName("wa_message_id").IsRequired();
            entity.HasIndex(message => message.WaMessageId).IsUnique();
            entity.Property(message => message.Direction).HasColumnName("direction").IsRequired();
            entity.Property(message => message.Type).HasColumnName("type").IsRequired();
            entity.Property(message => message.Body).HasColumnName("body").IsRequired();
            entity.Property(message => message.ReceivedAt).HasColumnName("received_at");
            entity.Property(message => message.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(message => message.ProcessedAt).HasColumnName("processed_at");
            entity.HasIndex(message => new { message.LeadId, message.ReceivedAt })
                .HasDatabaseName("messages_lead_received_idx").IsDescending(false, true);
            entity.HasOne(message => message.Lead).WithMany(lead => lead.Messages)
                .HasForeignKey(message => message.LeadId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Qualification>(entity =>
        {
            entity.ToTable("qualifications");
            entity.HasKey(qualification => qualification.Id);
            entity.Property(qualification => qualification.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(qualification => qualification.LeadId).HasColumnName("lead_id");
            entity.Property(qualification => qualification.Model).HasColumnName("model").IsRequired();
            entity.Property(qualification => qualification.PromptVersion).HasColumnName("prompt_version").IsRequired();
            entity.Property(qualification => qualification.Intent).HasColumnName("intent").IsRequired();
            entity.Property(qualification => qualification.Score).HasColumnName("score");
            entity.Property(qualification => qualification.NextAction).HasColumnName("next_action").IsRequired();
            entity.Property(qualification => qualification.Confidence).HasColumnName("confidence").HasPrecision(3, 2);
            entity.Property(qualification => qualification.SuggestedReply).HasColumnName("suggested_reply");
            entity.Property(qualification => qualification.MissingInfo).HasColumnName("missing_info").HasColumnType("jsonb").IsRequired();
            entity.Property(qualification => qualification.ForcedHandoff).HasColumnName("forced_handoff");
            entity.Property(qualification => qualification.ForcedReason).HasColumnName("forced_reason");
            entity.Property(qualification => qualification.RawResponse).HasColumnName("raw_response").IsRequired();
            entity.Property(qualification => qualification.LatencyMs).HasColumnName("latency_ms");
            entity.Property(qualification => qualification.InputTokens).HasColumnName("input_tokens");
            entity.Property(qualification => qualification.OutputTokens).HasColumnName("output_tokens");
            entity.Property(qualification => qualification.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.Property(qualification => qualification.ActionedAt).HasColumnName("actioned_at");
            entity.Property(qualification => qualification.ActionStatus).HasColumnName("action_status").HasDefaultValue(QualificationActionStatus.PendingApproval).IsRequired();
            entity.Property(qualification => qualification.ActionReason).HasColumnName("action_reason");
            entity.Property(qualification => qualification.ApprovedAt).HasColumnName("approved_at");
            entity.Property(qualification => qualification.OptInConfirmedAt).HasColumnName("opt_in_confirmed_at");
            entity.Property(qualification => qualification.WhatsAppMessageId).HasColumnName("whatsapp_message_id");
            entity.Property(qualification => qualification.ActionErrorId).HasColumnName("action_error_id");
            entity.HasIndex(qualification => new { qualification.LeadId, qualification.CreatedAt })
                .HasDatabaseName("qualifications_lead_created_idx").IsDescending(false, true);
            entity.HasIndex(qualification => new { qualification.ActionStatus, qualification.CreatedAt })
                .HasDatabaseName("qualifications_action_status_created_idx");
            entity.HasOne(qualification => qualification.Lead).WithMany(lead => lead.Qualifications)
                .HasForeignKey(qualification => qualification.LeadId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
