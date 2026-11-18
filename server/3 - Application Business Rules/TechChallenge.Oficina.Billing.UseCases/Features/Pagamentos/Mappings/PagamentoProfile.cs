using AutoMapper;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos;
using TechChallenge.Oficina.Billing.Entities.Features.Pagamentos.Enums;
using TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.ViewModels;

namespace TechChallenge.Oficina.Billing.UseCases.Features.Pagamentos.Mappings;

public sealed class PagamentoProfile : Profile
{
    public PagamentoProfile()
    {
        CreateMap<Pagamento, PagamentoViewModel>()
            .ForMember(destino => destino.Status, configuracao => configuracao.MapFrom(origem => (int)origem.Status))
            .ForMember(destino => destino.StatusDescricao, configuracao => configuracao.MapFrom(origem => origem.Status.ObterDescricao()))
            .ForMember(destino => destino.Provedor, configuracao => configuracao.MapFrom(origem => origem.Provedor.ToString()));
    }
}
