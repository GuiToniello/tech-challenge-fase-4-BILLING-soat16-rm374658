using AutoMapper;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Orcamentos.Mappings;

public sealed class OrcamentoProfile : Profile
{
    public OrcamentoProfile()
    {
        CreateMap<OrcamentoServico, OrcamentoServicoViewModel>();

        CreateMap<Orcamento, OrcamentoViewModel>()
            .ForMember(destino => destino.Status, configuracao => configuracao.MapFrom(origem => (int)origem.Status))
            .ForMember(destino => destino.StatusDescricao, configuracao => configuracao.MapFrom(origem => origem.Status.ObterDescricao()));
    }
}
